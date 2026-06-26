# ---------------------------------------------------------------------------
# Prometheus via Helm (kube-prometheus-stack)
# ---------------------------------------------------------------------------
resource "helm_release" "prometheus" {
  name       = "prometheus"
  repository = "https://prometheus-community.github.io/helm-charts"
  chart      = "kube-prometheus-stack"
  version    = "58.0.0"
  namespace  = kubernetes_namespace.cardmgmt.metadata[0].name

  # Grafana settings (dark theme, admin creds)
  set {
    name  = "grafana.adminUser"
    value = "admin"
  }

  set_sensitive {
    name  = "grafana.adminPassword"
    value = "admin"
  }

  set {
    name  = "grafana.defaultDashboardsEnabled"
    value = "true"
  }

  set {
    name  = "grafana.grafana\\.ini.users.default_theme"
    value = "dark"
  }

  set {
    name  = "grafana.service.type"
    value = "ClusterIP"
  }

  # Prometheus settings
  set {
    name  = "prometheus.prometheusSpec.retention"
    value = "15d"
  }

  set {
    name  = "prometheus.prometheusSpec.storageSpec.volumeClaimTemplate.spec.resources.requests.storage"
    value = "10Gi"
  }

  # Additional scrape config for the card management app
  values = [
    yamlencode({
      prometheus = {
        prometheusSpec = {
          additionalScrapeConfigs = [
            {
              job_name        = "card-management-api"
              metrics_path    = "/metrics"
              scrape_interval = "5s"
              static_configs = [
                {
                  targets = ["card-management-app.cardmgmt.svc.cluster.local:8080"]
                  labels = {
                    service     = "card-management"
                    environment = var.environment
                  }
                }
              ]
            }
          ]
        }
      }
      grafana = {
        dashboardProviders = {
          "dashboardproviders.yaml" = {
            apiVersion = 1
            providers = [
              {
                name            = "card-management"
                orgId           = 1
                folder          = "Card Management"
                type            = "file"
                disableDeletion = false
                editable        = true
                options = {
                  path = "/var/lib/grafana/dashboards/card-management"
                }
              }
            ]
          }
        }
        dashboardsConfigMaps = {
          card-management = "grafana-dashboards"
        }
      }
    })
  ]

  depends_on = [azurerm_kubernetes_cluster.aks]
}

# ---------------------------------------------------------------------------
# Grafana Dashboard ConfigMap
# ---------------------------------------------------------------------------
resource "kubernetes_config_map" "grafana_dashboards" {
  metadata {
    name      = "grafana-dashboards"
    namespace = kubernetes_namespace.cardmgmt.metadata[0].name
    labels = {
      grafana_dashboard = "1"
    }
  }

  data = {
    "red-use-dashboard.json" = file("${path.module}/../observability/grafana/dashboards/red-use-dashboard.json")
  }
}
