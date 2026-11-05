FROM node:22-bookworm-slim AS ui
WORKDIR /work/web
COPY web/package*.json ./
RUN --mount=type=secret,id=build_ca if [ -f /run/secrets/build_ca ]; then export NODE_EXTRA_CA_CERTS=/run/secrets/build_ca; fi; npm ci
COPY web/ ./
RUN npm run build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /work
COPY src/Billing.Api/ src/Billing.Api/
COPY --from=ui /work/src/Billing.Api/wwwroot/ src/Billing.Api/wwwroot/
RUN --mount=type=secret,id=build_ca if [ -f /run/secrets/build_ca ]; then export SSL_CERT_FILE=/run/secrets/build_ca; fi; dotnet publish src/Billing.Api -c Release -o /out
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
USER app
ENTRYPOINT ["dotnet", "Billing.Api.dll"]
