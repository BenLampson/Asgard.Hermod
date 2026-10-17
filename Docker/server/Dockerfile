FROM mcr.microsoft.com/dotnet/aspnet:10.0.12@sha256:222759b391a1aaf241166672c8f99b2d4ada452e7b5319f3c6e8f265a37b5ad4
WORKDIR /app
COPY publish/ ./
ENV ASPNETCORE_ENVIRONMENT=Production
USER 1654
EXPOSE 5086
ENTRYPOINT ["dotnet", "Asgard.Hermod.Server.Starter.dll"]
