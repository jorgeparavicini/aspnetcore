// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if NET7_0_OR_GREATER // IOutputCacheStore only exists from net7

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Microsoft.Extensions.Caching.StackExchangeRedis;

internal class RedisOutputCacheStore : IOutputCacheStore, IOutputCacheBufferWriterStore, IDisposable
{
    private readonly RedisCacheOptions _options;
    private readonly ILogger _logger;
    private readonly RedisKey _valueKeyPrefix, _tagKeyPrefix, _tagMasterKey;
    private readonly RedisKey[] _tagMasterKeyArray; // for use with Lua if needed (to avoid array allocs)
    private readonly SemaphoreSlim _connectionLock = new SemaphoreSlim(initialCount: 1, maxCount: 1);

    private bool _disposed;
    private volatile IDatabase? _cache;
    private long _lastConnectTicks = DateTimeOffset.UtcNow.Ticks;
    private long _firstErrorTimeTicks;
    private long _previousErrorTimeTicks;
    private bool _useMultiExec, _use62Features;

    // Never reconnect within 60 seconds of the last attempt to connect or reconnect.
    private readonly TimeSpan ReconnectMinInterval = TimeSpan.FromSeconds(60);
    // Only reconnect if errors have occurred for at least the last 30 seconds.
    // This count resets if there are no errors for 30 seconds
    private readonly TimeSpan ReconnectErrorThreshold = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Initializes a new instance of <see cref="RedisCache"/>.
    /// </summary>
    /// <param name="optionsAccessor">The configuration options.</param>
    public RedisOutputCacheStore(IOptions<RedisCacheOptions> optionsAccessor) // TODO: OC-specific options?
        : this(optionsAccessor, Logging.Abstractions.NullLoggerFactory.Instance.CreateLogger<RedisCache>())
    {
    }

#if DEBUG
    internal async ValueTask<string> GetConfigurationInfoAsync(CancellationToken cancellationToken = default)
    {
        await ConnectAsync(cancellationToken).ConfigureAwait(false);
        return $"redis output-cache; MULTI/EXEC: {_useMultiExec}, v6.2+: {_use62Features}";
    }
#endif

    /// <summary>
    /// Initializes a new instance of <see cref="RedisCache"/>.
    /// </summary>
    /// <param name="optionsAccessor">The configuration options.</param>
    /// <param name="logger">The logger.</param>
    internal RedisOutputCacheStore(IOptions<RedisCacheOptions> optionsAccessor, ILogger logger)
    {
        ArgumentNullThrowHelper.ThrowIfNull(optionsAccessor);
        ArgumentNullThrowHelper.ThrowIfNull(logger);

        _options = optionsAccessor.Value;
        _logger = logger;

        // This allows partitioning a single backend cache for use with multiple apps/services.

        // SE.Redis allows efficient append of key-prefix scenarios, but we can help it
        // avoid some work/allocations by forcing the key-prefix to be a byte[]; SE.Redis
        // would do this itself anyway, using UTF8
        _valueKeyPrefix = (RedisKey)Encoding.UTF8.GetBytes(_options.InstanceName + "__MSOCV_");
        _tagKeyPrefix = (RedisKey)Encoding.UTF8.GetBytes(_options.InstanceName + "__MSOCT_");
        _tagMasterKey = (RedisKey)Encoding.UTF8.GetBytes(_options.InstanceName + "__MSOCT");
        _tagMasterKeyArray = new[] { _tagMasterKey };
    }

    async ValueTask IOutputCacheStore.EvictByTagAsync(string tag, CancellationToken cancellationToken)
    {
        var cache = await ConnectAsync(cancellationToken).ConfigureAwait(false);
        Debug.Assert(cache is not null);

        // we'll use fire-and-forget on individual deletes, relying on the paging mechanism
        // of ZSCAN to detect fundamental connection problems - so failure will still be reported
        const CommandFlags DeleteFlags = CommandFlags.FireAndForget;

        var tagKey = GetTagKey(tag);
        await foreach (var entry in cache.SortedSetScanAsync(tagKey).WithCancellation(cancellationToken))
        {
            await cache.KeyDeleteAsync(GetValueKey((string)entry.Element!), DeleteFlags).ConfigureAwait(false);
            await cache.SortedSetRemoveAsync(tagKey, entry.Element, DeleteFlags).ConfigureAwait(false);
        }
    }

    private RedisKey GetValueKey(string key)
        => _valueKeyPrefix.Append(key);

    private RedisKey GetTagKey(string tag)
        => _tagKeyPrefix.Append(tag);

    async ValueTask<byte[]?> IOutputCacheStore.GetAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentNullThrowHelper.ThrowIfNull(key);

        var cache = await ConnectAsync(cancellationToken).ConfigureAwait(false);
        Debug.Assert(cache is not null);

        try
        {
            return (byte[]?)(await cache.StringGetAsync(GetValueKey(key)).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            OnRedisError(ex, cache);
            throw;
        }
    }

    async ValueTask<bool> IOutputCacheBufferWriterStore.GetAsync(string key, IBufferWriter<byte> destination, CancellationToken cancellationToken)
    {
        ArgumentNullThrowHelper.ThrowIfNull(key);
        ArgumentNullThrowHelper.ThrowIfNull(destination);

        var cache = await ConnectAsync(cancellationToken).ConfigureAwait(false);
        Debug.Assert(cache is not null);

        Lease<byte>? result = null;
        try
        {
            result = await cache.StringGetLeaseAsync(GetValueKey(key)).ConfigureAwait(false);
            if (result is null)
            {
                return false;
            }

            destination.Write(result.Span);
            return true;
        }
        catch (Exception ex)
        {
            result?.Dispose();
            OnRedisError(ex, cache);
            throw;
        }
    }

    ValueTask IOutputCacheStore.SetAsync(string key, byte[] value, string[]? tags, TimeSpan validFor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(value);
        return ((IOutputCacheStore)this).SetAsync(key, new ReadOnlySequence<byte>(value), tags.AsMemory(), validFor, cancellationToken);
    }

    async ValueTask IOutputCacheStore.SetAsync(string key, ReadOnlySequence<byte> value, ReadOnlyMemory<string> tags, TimeSpan validFor, CancellationToken cancellationToken)
    {
        var cache = await ConnectAsync(cancellationToken).ConfigureAwait(false);
        Debug.Assert(cache is not null);

        byte[]? leased = null;
        ReadOnlyMemory<byte> singleChunk;
        if (value.IsSingleSegment)
        {
            singleChunk = value.First;
        }
        else
        {
            int len = checked((int)value.Length);
            leased = ArrayPool<byte>.Shared.Rent(len);
            value.CopyTo(leased);
            singleChunk = new(leased, 0, len);
        }

        await cache.StringSetAsync(GetValueKey(key), singleChunk, validFor).ConfigureAwait(false);

        if (!tags.IsEmpty)
        {
            long expiryTimestamp = GetExpirationTimestamp(validFor);
            var len = tags.Length;

            // tags are secondary; to avoid latency costs, we'll use fire-and-forget when adding tags - this does
            // mean that in theory tag-related error may go undetected, but: this is an acceptable trade-off
            const CommandFlags TagCommandFlags = CommandFlags.FireAndForget;

            for (int i = 0; i < len; i++) // can't use span in async method, so: eat a little overhead here
            {
                var tag = tags.Span[i];
                if (_use62Features)
                {
                    await cache.SortedSetAddAsync(_tagMasterKey, tag, expiryTimestamp, SortedSetWhen.GreaterThan, TagCommandFlags).ConfigureAwait(false);
                }
                else
                {
                    // semantic equivalent of ZADD GT
                    const string ZADD_GT = """
                    local oldScore = tonumber(redis.call('ZSCORE', KEYS[1], ARGV[2]))
                    if oldScore == nil or oldScore < tonumber(ARGV[1]) then
                        redis.call('ZADD', KEYS[1], ARGV[1], ARGV[2])
                    end
                    """;

                    // note we're not sharing an ARGV array between tags here because then we'd need to wait on latency to avoid conflicts;
                    // in reality most caches have very limited tags (if any), so this is not perceived as an issue
                    await cache.ScriptEvaluateAsync(ZADD_GT, _tagMasterKeyArray, new RedisValue[] { expiryTimestamp, tag }, TagCommandFlags).ConfigureAwait(false);
                }
                await cache.SortedSetAddAsync(GetTagKey(tag), key, expiryTimestamp, SortedSetWhen.Always, TagCommandFlags).ConfigureAwait(false);
            }
        }

        // only return lease on success
        if (leased is not null)
        {
            ArrayPool<byte>.Shared.Return(leased);
        }
    }

    // note that by necessity we're interleaving two time systems here; the local time, and the
    // time according to redis (and used internally for redis TTLs); in reality, if we disagree
    // on time, we have bigger problems, so: this will have to suffice - we cannot reasonably
    // push our in-proc time into out-of-proc redis
    // TODO: TimeProvider? ISystemClock?
    private static long GetExpirationTimestamp(TimeSpan timeout) =>
        (long)((DateTime.UtcNow + timeout) - DateTime.UnixEpoch).TotalMilliseconds;

    private ValueTask<IDatabase> ConnectAsync(CancellationToken token = default)
    {
        CheckDisposed();
        token.ThrowIfCancellationRequested();

        var cache = _cache;
        if (cache is not null)
        {
            Debug.Assert(_cache is not null);
            return new(cache);
        }
        return ConnectSlowAsync(token);
    }
    private async ValueTask<IDatabase> ConnectSlowAsync(CancellationToken token)
    {
        await _connectionLock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var cache = _cache;
            if (cache is null)
            {
                IConnectionMultiplexer connection;
                if (_options.ConnectionMultiplexerFactory is null)
                {
                    if (_options.ConfigurationOptions is not null)
                    {
                        connection = await ConnectionMultiplexer.ConnectAsync(_options.ConfigurationOptions).ConfigureAwait(false);
                    }
                    else
                    {
                        connection = await ConnectionMultiplexer.ConnectAsync(_options.Configuration!).ConfigureAwait(false);
                    }
                }
                else
                {
                    connection = await _options.ConnectionMultiplexerFactory().ConfigureAwait(false);
                }

                PrepareConnection(connection);
                cache = _cache = connection.GetDatabase();
            }
            Debug.Assert(_cache is not null);
            return cache;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ReleaseConnection(Interlocked.Exchange(ref _cache, null));
    }

    private void OnRedisError(Exception exception, IDatabase cache)
    {
        if (_options.UseForceReconnect && (exception is RedisConnectionException or SocketException))
        {
            var utcNow = DateTimeOffset.UtcNow;
            var previousConnectTime = ReadTimeTicks(ref _lastConnectTicks);
            TimeSpan elapsedSinceLastReconnect = utcNow - previousConnectTime;

            // We want to limit how often we perform this top-level reconnect, so we check how long it's been since our last attempt.
            if (elapsedSinceLastReconnect < ReconnectMinInterval)
            {
                return;
            }

            var firstErrorTime = ReadTimeTicks(ref _firstErrorTimeTicks);
            if (firstErrorTime == DateTimeOffset.MinValue)
            {
                // note: order/timing here (between the two fields) is not critical
                WriteTimeTicks(ref _firstErrorTimeTicks, utcNow);
                WriteTimeTicks(ref _previousErrorTimeTicks, utcNow);
                return;
            }

            TimeSpan elapsedSinceFirstError = utcNow - firstErrorTime;
            TimeSpan elapsedSinceMostRecentError = utcNow - ReadTimeTicks(ref _previousErrorTimeTicks);

            bool shouldReconnect =
                    elapsedSinceFirstError >= ReconnectErrorThreshold // Make sure we gave the multiplexer enough time to reconnect on its own if it could.
                    && elapsedSinceMostRecentError <= ReconnectErrorThreshold; // Make sure we aren't working on stale data (e.g. if there was a gap in errors, don't reconnect yet).

            // Update the previousErrorTime timestamp to be now (e.g. this reconnect request).
            WriteTimeTicks(ref _previousErrorTimeTicks, utcNow);

            if (!shouldReconnect)
            {
                return;
            }

            WriteTimeTicks(ref _firstErrorTimeTicks, DateTimeOffset.MinValue);
            WriteTimeTicks(ref _previousErrorTimeTicks, DateTimeOffset.MinValue);

            // wipe the shared field, but *only* if it is still the cache we were
            // thinking about (once it is null, the next caller will reconnect)
            ReleaseConnection(Interlocked.CompareExchange(ref _cache, null, cache));
        }
    }

    private void PrepareConnection(IConnectionMultiplexer connection)
    {
        WriteTimeTicks(ref _lastConnectTicks, DateTimeOffset.UtcNow);
        ValidateServerFeatures(connection);
        TryRegisterProfiler(connection);
    }

    private void ValidateServerFeatures(IConnectionMultiplexer connection)
    {
        int serverCount = 0, standaloneCount = 0, v62_Count = 0;
        foreach (var ep in connection.GetEndPoints())
        {
            var server = connection.GetServer(ep);
            if (server is null)
            {
                continue; // wat?
            }
            serverCount++;
            if (server.ServerType == ServerType.Standalone)
            {
                standaloneCount++;
            }
            if (server.Features.SortedSetRangeStore) // just a random v6.2 feature
            {
                v62_Count++;
            }
        }
        _useMultiExec = serverCount == standaloneCount;
        _use62Features = serverCount == v62_Count;
    }

    private void TryRegisterProfiler(IConnectionMultiplexer connection)
    {
        _ = connection ?? throw new InvalidOperationException($"{nameof(connection)} cannot be null.");

        if (_options.ProfilingSession is not null)
        {
            connection.RegisterProfiler(_options.ProfilingSession);
        }
    }

    private static void WriteTimeTicks(ref long field, DateTimeOffset value)
    {
        var ticks = value == DateTimeOffset.MinValue ? 0L : value.UtcTicks;
        Volatile.Write(ref field, ticks); // avoid torn values
    }

    private void CheckDisposed()
    {
        ObjectDisposedThrowHelper.ThrowIf(_disposed, this);
    }

    private static DateTimeOffset ReadTimeTicks(ref long field)
    {
        var ticks = Volatile.Read(ref field); // avoid torn values
        return ticks == 0 ? DateTimeOffset.MinValue : new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    static void ReleaseConnection(IDatabase? cache)
    {
        var connection = cache?.Multiplexer;
        if (connection is not null)
        {
            try
            {
                connection.Close();
                connection.Dispose();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }
    }
}
#endif
