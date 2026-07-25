# Bake provisioning + dashboards in so Coolify needs no host bind mounts.
FROM grafana/grafana:latest
COPY provisioning /etc/grafana/provisioning
COPY dashboards /var/lib/grafana/dashboards
