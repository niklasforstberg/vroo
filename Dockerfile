# Build frontend
FROM node:22-alpine AS frontend-build
WORKDIR /app
COPY clientapp/package*.json ./
RUN npm ci
COPY clientapp/ ./
RUN npm run build

# Build backend
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["vroo.csproj", "./"]
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app/publish

# Final image
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
COPY --from=frontend-build /app/dist ./wwwroot
ENTRYPOINT ["dotnet", "Vroo.dll"]
