FROM node:22-alpine AS web
WORKDIR /web
COPY finsim-web/package*.json ./
RUN npm ci
COPY finsim-web/ ./
RUN npm run build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

COPY src/FinSim.Domain/FinSim.Domain.csproj          src/FinSim.Domain/
COPY src/FinSim.Application/FinSim.Application.csproj src/FinSim.Application/
COPY src/FinSim.Infrastructure/FinSim.Infrastructure.csproj src/FinSim.Infrastructure/
COPY src/FinSim.Api/FinSim.Api.csproj                src/FinSim.Api/

RUN dotnet restore src/FinSim.Api/FinSim.Api.csproj

COPY src/ src/
COPY --from=web /web/dist/ src/FinSim.Api/wwwroot/

RUN dotnet publish src/FinSim.Api/FinSim.Api.csproj \
    -c Release \
    -o /app \
    --no-restore


FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "FinSim.Api.dll"]