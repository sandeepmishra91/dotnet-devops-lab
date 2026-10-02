FROM docker:cli AS dockercli
FROM jenkins/jenkins:lts-jdk21
USER root
COPY --from=dockercli /usr/local/bin/docker /usr/local/bin/docker
COPY --from=dockercli /usr/local/libexec/docker/cli-plugins/ /usr/local/libexec/docker/cli-plugins/
ARG KIND_VERSION=v0.33.0
ARG KUBECTL_VERSION
RUN test -n "$KUBECTL_VERSION" \
    && case "$(uname -m)" in \
       x86_64) arch=amd64 ;; \
       aarch64) arch=arm64 ;; \
       *) exit 1 ;; \
       esac \
    && curl -fsSL "https://kind.sigs.k8s.io/dl/${KIND_VERSION}/kind-linux-${arch}" -o /usr/local/bin/kind \
    && curl -fsSL "https://dl.k8s.io/release/${KUBECTL_VERSION}/bin/linux/${arch}/kubectl" -o /usr/local/bin/kubectl \
    && curl -fsSL "https://dl.k8s.io/release/${KUBECTL_VERSION}/bin/linux/${arch}/kubectl.sha256" -o /tmp/kubectl.sha256 \
    && echo "$(cat /tmp/kubectl.sha256)  /usr/local/bin/kubectl" | sha256sum -c - \
    && chmod +x /usr/local/bin/kind /usr/local/bin/kubectl \
    && rm /tmp/kubectl.sha256
USER jenkins
