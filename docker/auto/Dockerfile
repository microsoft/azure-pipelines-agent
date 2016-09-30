FROM buildpack-deps:jessie-scm

RUN apt-get update && apt-get install -y --no-install-recommends \
    apt-transport-https \
    libunwind8 \
 && rm -rf /var/lib/apt/lists/*

RUN set -x \
 && curl -L https://github.com/Yelp/dumb-init/releases/download/v1.1.3/dumb-init_1.1.3_amd64 -o /usr/local/bin/dumb-init \
 && chmod +x /usr/local/bin/dumb-init \
 && /usr/local/bin/dumb-init -V

RUN adduser --system --group vsts
WORKDIR /home/vsts

COPY ./start.sh .
RUN chown vsts:vsts start.sh

ENTRYPOINT ["/usr/local/bin/dumb-init", "--"]
CMD ["./start.sh"]
