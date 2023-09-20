FROM alpine

RUN apk update
RUN apk upgrade
RUN apk add bash git curl nodejs npm openssl dotnet6-sdk icu-libs krb5-libs libgcc libintl libssl1.1 libstdc++ zlib libgdiplus

LABEL com.azure.dev.pipelines.agent.handler.node.path=/usr/bin/node

CMD /usr/bin/node
