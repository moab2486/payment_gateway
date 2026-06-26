# ---------------------------------------------------------------------------
# Kubernetes Namespace
# ---------------------------------------------------------------------------
resource "kubernetes_namespace" "cardmgmt" {
  metadata {
    name = "cardmgmt"
    labels = {
      "app.kubernetes.io/part-of" = "card-management"
      environment                 = var.environment
    }
  }

  depends_on = [azurerm_kubernetes_cluster.aks]
}

# ---------------------------------------------------------------------------
# Kubernetes Secrets
# ---------------------------------------------------------------------------
resource "kubernetes_secret" "postgres_credentials" {
  metadata {
    name      = "postgres-credentials"
    namespace = kubernetes_namespace.cardmgmt.metadata[0].name
  }

  data = {
    CONNECTION_STRING = "Host=${azurerm_postgresql_flexible_server.main.fqdn};Port=5432;Database=cardmanagement;Username=${var.postgres_admin_username};Password=${var.postgres_admin_password};SslMode=Require"
  }

  type = "Opaque"
}

resource "kubernetes_secret" "redis_credentials" {
  metadata {
    name      = "redis-credentials"
    namespace = kubernetes_namespace.cardmgmt.metadata[0].name
  }

  data = {
    CONNECTION_STRING = "${azurerm_redis_cache.main.hostname}:${azurerm_redis_cache.main.ssl_port},password=${azurerm_redis_cache.main.primary_access_key},ssl=True,abortConnect=False"
  }

  type = "Opaque"
}

# ---------------------------------------------------------------------------
# Kafka via Helm (Bitnami)
# ---------------------------------------------------------------------------
resource "helm_release" "kafka" {
  name       = "kafka"
  repository = "https://charts.bitnami.com/bitnami"
  chart      = "kafka"
  version    = "28.0.0"
  namespace  = kubernetes_namespace.cardmgmt.metadata[0].name

  set {
    name  = "kraft.enabled"
    value = "true"
  }

  set {
    name  = "controller.replicaCount"
    value = "1"
  }

  set {
    name  = "broker.replicaCount"
    value = "1"
  }

  set {
    name  = "listeners.client.protocol"
    value = "PLAINTEXT"
  }

  set {
    name  = "provisioning.enabled"
    value = "false"
  }

  set {
    name  = "persistence.size"
    value = "10Gi"
  }
}

# ---------------------------------------------------------------------------
# WAHA (WhatsApp) Deployment
# ---------------------------------------------------------------------------
resource "kubernetes_deployment" "waha" {
  metadata {
    name      = "waha"
    namespace = kubernetes_namespace.cardmgmt.metadata[0].name
    labels = {
      app = "waha"
    }
  }

  spec {
    replicas = 1

    selector {
      match_labels = {
        app = "waha"
      }
    }

    template {
      metadata {
        labels = {
          app = "waha"
        }
      }

      spec {
        container {
          name  = "waha"
          image = "devlikeapro/waha:latest"

          port {
            container_port = 3000
          }

          env {
            name  = "WHATSAPP_DEFAULT_ENGINE"
            value = "WEBJS"
          }

          env {
            name  = "WAHA_PRINT_QR"
            value = "true"
          }

          readiness_probe {
            http_get {
              path = "/api/sessions"
              port = 3000
            }
            initial_delay_seconds = 10
            period_seconds        = 10
          }

          liveness_probe {
            http_get {
              path = "/api/sessions"
              port = 3000
            }
            initial_delay_seconds = 20
            period_seconds        = 15
          }

          resources {
            requests = {
              memory = "256Mi"
              cpu    = "250m"
            }
            limits = {
              memory = "512Mi"
              cpu    = "500m"
            }
          }
        }
      }
    }
  }
}

resource "kubernetes_service" "waha" {
  metadata {
    name      = "waha"
    namespace = kubernetes_namespace.cardmgmt.metadata[0].name
  }

  spec {
    selector = {
      app = "waha"
    }

    port {
      port        = 3000
      target_port = 3000
    }

    type = "ClusterIP"
  }
}

# ---------------------------------------------------------------------------
# Card Management App Deployment
# ---------------------------------------------------------------------------
resource "kubernetes_deployment" "app" {
  metadata {
    name      = "card-management-app"
    namespace = kubernetes_namespace.cardmgmt.metadata[0].name
    labels = {
      app = "card-management-app"
    }
  }

  spec {
    replicas = 2

    selector {
      match_labels = {
        app = "card-management-app"
      }
    }

    template {
      metadata {
        labels = {
          app = "card-management-app"
        }
        annotations = {
          "prometheus.io/scrape" = "true"
          "prometheus.io/port"   = "8080"
          "prometheus.io/path"   = "/metrics"
        }
      }

      spec {
        container {
          name  = "app"
          image = "${azurerm_container_registry.acr.login_server}/card-management-app:${var.app_image_tag}"

          port {
            name           = "http"
            container_port = 8080
          }

          port {
            name           = "tcp-iso8583"
            container_port = 9090
          }

          env {
            name = "DATABASE__CONNECTION_STRING"
            value_from {
              secret_key_ref {
                name = kubernetes_secret.postgres_credentials.metadata[0].name
                key  = "CONNECTION_STRING"
              }
            }
          }

          env {
            name  = "TCP__LISTENER_PORT"
            value = "9090"
          }

          env {
            name  = "KAFKA__BOOTSTRAP_SERVERS"
            value = "kafka:9092"
          }

          env {
            name  = "KAFKA__TOPIC_PREFIX"
            value = "cardmgmt.events"
          }

          env {
            name  = "KAFKA__CLIENT_ID"
            value = "card-management-api"
          }

          env {
            name = "DeveloperPortal__ApiKeyCache__RedisConnectionString"
            value_from {
              secret_key_ref {
                name = kubernetes_secret.redis_credentials.metadata[0].name
                key  = "CONNECTION_STRING"
              }
            }
          }

          env {
            name  = "Notifications__WhatsApp__BaseUrl"
            value = "http://waha:3000"
          }

          readiness_probe {
            http_get {
              path = "/health"
              port = 8080
            }
            initial_delay_seconds = 15
            period_seconds        = 10
          }

          liveness_probe {
            http_get {
              path = "/health"
              port = 8080
            }
            initial_delay_seconds = 30
            period_seconds        = 15
          }

          resources {
            requests = {
              memory = "256Mi"
              cpu    = "250m"
            }
            limits = {
              memory = "512Mi"
              cpu    = "1000m"
            }
          }
        }
      }
    }
  }

  depends_on = [
    helm_release.kafka,
    kubernetes_deployment.waha,
    azurerm_postgresql_flexible_server_database.cardmanagement,
    azurerm_redis_cache.main
  ]
}

resource "kubernetes_service" "app" {
  metadata {
    name      = "card-management-app"
    namespace = kubernetes_namespace.cardmgmt.metadata[0].name
  }

  spec {
    selector = {
      app = "card-management-app"
    }

    port {
      name        = "http"
      port        = 8080
      target_port = 8080
    }

    port {
      name        = "tcp-iso8583"
      port        = 9090
      target_port = 9090
    }

    type = "ClusterIP"
  }
}

# ---------------------------------------------------------------------------
# Nginx Ingress Controller via Helm
# ---------------------------------------------------------------------------
resource "helm_release" "nginx_ingress" {
  name       = "ingress-nginx"
  repository = "https://kubernetes.github.io/ingress-nginx"
  chart      = "ingress-nginx"
  version    = "4.10.0"
  namespace  = "ingress-nginx"

  create_namespace = true

  set {
    name  = "controller.replicaCount"
    value = "2"
  }

  set {
    name  = "controller.service.annotations.service\\.beta\\.kubernetes\\.io/azure-load-balancer-health-probe-request-path"
    value = "/healthz"
  }

  set {
    name  = "controller.metrics.enabled"
    value = "true"
  }

  set {
    name  = "controller.metrics.serviceMonitor.enabled"
    value = "false"
  }
}

# ---------------------------------------------------------------------------
# Ingress resource for the app
# ---------------------------------------------------------------------------
resource "kubernetes_ingress_v1" "app" {
  metadata {
    name      = "card-management-ingress"
    namespace = kubernetes_namespace.cardmgmt.metadata[0].name
    annotations = {
      "nginx.ingress.kubernetes.io/ssl-redirect"    = "true"
      "nginx.ingress.kubernetes.io/proxy-body-size" = "10m"
    }
  }

  spec {
    ingress_class_name = "nginx"

    tls {
      hosts       = ["cardmgmt.${var.environment}.example.com"]
      secret_name = "app-tls"
    }

    rule {
      host = "cardmgmt.${var.environment}.example.com"

      http {
        path {
          path      = "/"
          path_type = "Prefix"

          backend {
            service {
              name = kubernetes_service.app.metadata[0].name
              port {
                number = 8080
              }
            }
          }
        }
      }
    }
  }

  depends_on = [helm_release.nginx_ingress]
}
