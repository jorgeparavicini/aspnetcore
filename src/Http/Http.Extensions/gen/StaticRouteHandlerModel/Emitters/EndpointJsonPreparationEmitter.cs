// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.AspNetCore.Http.RequestDelegateGenerator.StaticRouteHandlerModel.Emitters;

internal static class EndpointJsonPreparationEmitter
{
    internal static void EmitJsonPreparation(this Endpoint endpoint, CodeWriter codeWriter)
    {
        var serializerOptionsEmitted = false;
        if (endpoint.Response?.IsSerializableJsonResponse(out var responseType) == true)
        {
            var typeName = responseType.ToDisplayString(EmitterConstants.DisplayFormat);
            codeWriter.WriteLine("var serializerOptions = serviceProvider?.GetService<IOptions<JsonOptions>>()?.Value.SerializerOptions ?? new JsonOptions().SerializerOptions;");
            serializerOptionsEmitted = true;
            codeWriter.WriteLine($"var responseJsonTypeInfo =  (JsonTypeInfo<{typeName}>)serializerOptions.GetTypeInfo(typeof({typeName}));");
        }

        foreach (var parameter in endpoint.Parameters)
        {
            ProcessParameter(parameter, codeWriter, ref serializerOptionsEmitted);
            if (parameter is { Source: EndpointParameterSource.AsParameters, EndpointParameters: {} innerParameters })
            {
                foreach (var innerParameter in innerParameters)
                {
                    ProcessParameter(innerParameter, codeWriter, ref serializerOptionsEmitted);
                }
            }
        }

        static void ProcessParameter(EndpointParameter parameter, CodeWriter codeWriter, ref bool serializerOptionsEmitted)
        {
            if (parameter.Source != EndpointParameterSource.JsonBody && parameter.Source != EndpointParameterSource.JsonBodyOrService && parameter.Source != EndpointParameterSource.JsonBodyOrQuery)
            {
                return;
            }
            var typeName = parameter.Type.ToDisplayString(EmitterConstants.DisplayFormat);
            if (!serializerOptionsEmitted)
            {
                serializerOptionsEmitted = true;
                codeWriter.WriteLine("var serializerOptions = serviceProvider?.GetService<IOptions<JsonOptions>>()?.Value.SerializerOptions ?? new JsonOptions().SerializerOptions;");
            }
            codeWriter.WriteLine($"var {parameter.SymbolName}_JsonTypeInfo =  (JsonTypeInfo<{typeName}>)serializerOptions.GetTypeInfo(typeof({parameter.Type.ToDisplayString(EmitterConstants.DisplayFormatWithoutNullability)}));");
        }

    }

    internal static string EmitJsonResponse(this EndpointResponse endpointResponse)
    {
        if (endpointResponse.ResponseType != null &&
            (endpointResponse.ResponseType.IsSealed || endpointResponse.ResponseType.IsValueType))
        {
            return "httpContext.Response.WriteAsJsonAsync(result, responseJsonTypeInfo);";
        }
        return "GeneratedRouteBuilderExtensionsCore.WriteToResponseAsync(result, httpContext, responseJsonTypeInfo);";
    }
}
