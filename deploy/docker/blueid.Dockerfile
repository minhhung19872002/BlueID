# One image for every BlueID process; the first argument picks it:  docker run blueid/server web|api
# The account/admin portal (web/) is built here and served by BlueID.Web under /portal/.
FROM node:22.23.5-alpine3.24 AS portal
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY web/ ./
RUN npx vitest run && npx tsc -b --noEmit && npx vite build --outDir /portal

FROM mcr.microsoft.com/dotnet/sdk:10.0.404 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props BlueID.slnx .editorconfig ./
COPY src/ src/
RUN dotnet publish src/BlueID.Web -c Release -o /out/web --nologo \
 && dotnet publish src/BlueID.Api -c Release -o /out/api --nologo

FROM mcr.microsoft.com/dotnet/aspnet:10.0.15 AS runtime
# libgssapi-krb5-2: Npgsql probes GSS at start and logs an error without it.
RUN apt-get update && apt-get install -y --no-install-recommends libgssapi-krb5-2 && rm -rf /var/lib/apt/lists/*
RUN groupadd -g 10001 blueid && useradd -u 10001 -g blueid -M -s /usr/sbin/nologin blueid
COPY --from=build /out/ /app/
COPY --from=portal /portal /app/web/wwwroot/portal
COPY deploy/docker/blueid-entrypoint.sh /usr/local/bin/blueid
RUN chmod 0755 /usr/local/bin/blueid
USER blueid
ENV ASPNETCORE_HTTP_PORTS=8080 DOTNET_gcServer=1
EXPOSE 8080
HEALTHCHECK --interval=15s --timeout=3s --retries=5 CMD ["/usr/local/bin/blueid", "healthcheck"]
ENTRYPOINT ["/usr/local/bin/blueid"]
