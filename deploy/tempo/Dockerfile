# Bake the Tempo config so Coolify needs no host bind mount.
FROM grafana/tempo:latest
COPY tempo.yaml /etc/tempo/tempo.yaml
CMD ["-config.file=/etc/tempo/tempo.yaml"]
