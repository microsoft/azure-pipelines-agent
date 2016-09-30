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
WORKDIR /home/vsts/agent

ENV VSTS_AGENT_LOC https://github.com/Microsoft/vsts-agent/releases/download
ENV VSTS_AGENT_VER 2.106.0

RUN set -x \
 && curl -fSL  ${VSTS_AGENT_LOC}/v${VSTS_AGENT_VER}/vsts-agent-ubuntu.14.04-x64-$VSTS_AGENT_VER.tar.gz -o agent.tgz \
 && tar -xzf agent.tgz \
 && chown -R vsts:vsts . \
 && rm agent.tgz

COPY ./start.sh .
RUN chown vsts:vsts start.sh

ENTRYPOINT ["/usr/local/bin/dumb-init", "--"]
CMD ["./start.sh"]
