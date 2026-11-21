# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0.401 AS build
WORKDIR /src
COPY . .
RUN dotnet restore ./src/Company.Product.Service.Api/Company.Product.Service.Api.csproj -r linux-x64 -p:IncludeDatadogProfiler=true
RUN dotnet publish ./src/Company.Product.Service.Api/Company.Product.Service.Api.csproj -r linux-x64 -c Release --no-self-contained --no-restore -p:IncludeDatadogProfiler=true -o /app/publish \
    && test -s /app/publish/datadog/linux-x64/Datadog.Trace.ClrProfiler.Native.so

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0.12 AS runtime
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
ENV CORECLR_ENABLE_PROFILING=1 \
    CORECLR_PROFILER="{846F5F1C-F9AE-4B07-969E-05C26BC060D8}" \
    CORECLR_PROFILER_PATH=/app/datadog/linux-x64/Datadog.Trace.ClrProfiler.Native.so \
    DD_DOTNET_TRACER_HOME=/app/datadog \
    DD_TRACE_LOG_DIRECTORY=/tmp
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Company.Product.Service.Api.dll"]
