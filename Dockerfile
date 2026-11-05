FROM node:22-bookworm-slim AS ui
WORKDIR /work/web
COPY web/package*.json ./
RUN npm ci
COPY web/ ./
RUN npm run build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /work
COPY src/Billing.Api/ src/Billing.Api/
COPY --from=ui /work/src/Billing.Api/wwwroot/ src/Billing.Api/wwwroot/
RUN dotnet publish src/Billing.Api -c Release -o /out
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
USER app
ENTRYPOINT ["dotnet", "Billing.Api.dll"]
