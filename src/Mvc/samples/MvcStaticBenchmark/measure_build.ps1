# Path to your .csproj file
$PROJECT_PATH = "."

# Clean the project first
dotnet msbuild /t:Clean /p:Configuration=Release /p:BuildProjectReferences=$false $PROJECT_PATH

# Now build the project without measuring time to generate initial artifacts
dotnet msbuild /p:Configuration=Release /p:BuildProjectReferences=$false $PROJECT_PATH

# Repeat the build process 100 times and measure time
for ($i=0; $i -lt 100; $i++)
{
    Write-Host "Build iteration: $($i + 1)"

    # Clean the project
    dotnet msbuild /t:Clean /p:Configuration=Release /p:BuildProjectReferences=$false $PROJECT_PATH

    # Stop the compiler
    Stop-Process -Name VBCSCompiler -Force -ErrorAction SilentlyContinue

    # Measure build time
    Measure-Command { dotnet msbuild /p:Configuration=Release /p:BuildProjectReferences=$false $PROJECT_PATH } | Out-File -Append build_times.txt
}
