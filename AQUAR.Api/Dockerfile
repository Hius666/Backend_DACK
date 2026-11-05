# Build context: AQUAR.Api (Render Root Directory = AQUAR.Api)
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY AQUAR.Api.csproj ./
RUN dotnet restore AQUAR.Api.csproj
COPY . ./
RUN dotnet publish AQUAR.Api.csproj -c Release -o /out --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_ENVIRONMENT=Production
COPY --from=build --chown=app:app /out ./
USER app
# Metadata for local testing only. At runtime Render PORT takes precedence in Program.cs.
EXPOSE 8080
ENTRYPOINT ["dotnet", "AQUAR.Api.dll"]
