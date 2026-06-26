# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project files for restore
COPY CardManagement.sln ./
COPY src/CardManagement.Api/CardManagement.Api.csproj src/CardManagement.Api/
COPY src/CardManagement.Application/CardManagement.Application.csproj src/CardManagement.Application/
COPY src/CardManagement.Infrastructure/CardManagement.Infrastructure.csproj src/CardManagement.Infrastructure/
COPY src/CardManagement.Domain/CardManagement.Domain.csproj src/CardManagement.Domain/

# Restore NuGet packages
RUN dotnet restore CardManagement.sln

# Copy all source code
COPY src/ src/

# Build in Release configuration
RUN dotnet build -c Release --no-restore

# Publish the API project
RUN dotnet publish src/CardManagement.Api -c Release -o /app/publish --no-build

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Copy published output from build stage
COPY --from=build /app/publish .

# Set environment variables
ENV ASPNETCORE_URLS=http://+:8080

# Expose ports for REST API and TCP listener
EXPOSE 8080 9090

# Use non-root user for security
USER app

ENTRYPOINT ["dotnet", "CardManagement.Api.dll"]
