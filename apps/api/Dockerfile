FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore depends on project metadata, so source edits can reuse this layer.
COPY Directory.Build.props Directory.Packages.props global.json ./
COPY apps/api/src/SupportDesk.Domain/SupportDesk.Domain.csproj apps/api/src/SupportDesk.Domain/
COPY apps/api/src/SupportDesk.Application/SupportDesk.Application.csproj apps/api/src/SupportDesk.Application/
COPY apps/api/src/SupportDesk.Infrastructure/SupportDesk.Infrastructure.csproj apps/api/src/SupportDesk.Infrastructure/
COPY apps/api/src/SupportDesk.Presentation/SupportDesk.Presentation.csproj apps/api/src/SupportDesk.Presentation/
RUN dotnet restore apps/api/src/SupportDesk.Presentation/SupportDesk.Presentation.csproj

COPY apps/api/src/ apps/api/src/
RUN dotnet publish apps/api/src/SupportDesk.Presentation/SupportDesk.Presentation.csproj \
    --configuration Release --no-restore --output /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /out/ ./
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "SupportDesk.Presentation.dll"]
