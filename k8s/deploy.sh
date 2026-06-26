#!/usr/bin/env bash
set -euo pipefail

# Card Management System — Kubernetes Deployment Script
# Usage: ./k8s/deploy.sh [build|apply|delete]

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"
NAMESPACE="cardmgmt"
IMAGE_NAME="card-management-app"
IMAGE_TAG="latest"

usage() {
    echo "Usage: $0 {build|apply|delete|status}"
    echo ""
    echo "Commands:"
    echo "  build   - Build the Docker image for the app"
    echo "  apply   - Apply all Kubernetes manifests"
    echo "  delete  - Delete all resources in the cardmgmt namespace"
    echo "  status  - Show status of all pods and services"
    exit 1
}

cmd_build() {
    echo "🔨 Building Docker image: ${IMAGE_NAME}:${IMAGE_TAG}"
    docker build -t "${IMAGE_NAME}:${IMAGE_TAG}" -f "$PROJECT_ROOT/Dockerfile" "$PROJECT_ROOT"
    echo "✅ Image built successfully"
}

cmd_apply() {
    echo "🚀 Applying Kubernetes manifests..."

    # Populate TLS secret from local certs if they exist
    if [[ -f "$PROJECT_ROOT/nginx/certs/selfsigned.crt" && -f "$PROJECT_ROOT/nginx/certs/selfsigned.key" ]]; then
        echo "📜 Creating TLS secret from local certificates..."
        kubectl create namespace "$NAMESPACE" --dry-run=client -o yaml | kubectl apply -f -
        kubectl -n "$NAMESPACE" create secret tls nginx-tls \
            --cert="$PROJECT_ROOT/nginx/certs/selfsigned.crt" \
            --key="$PROJECT_ROOT/nginx/certs/selfsigned.key" \
            --dry-run=client -o yaml | kubectl apply -f -
    fi

    # Apply with kustomize
    kubectl apply -k "$SCRIPT_DIR"

    echo ""
    echo "✅ All resources applied. Waiting for pods..."
    kubectl -n "$NAMESPACE" rollout status deployment/postgres --timeout=60s || true
    kubectl -n "$NAMESPACE" rollout status deployment/kafka --timeout=90s || true
    kubectl -n "$NAMESPACE" rollout status deployment/redis --timeout=60s || true
    kubectl -n "$NAMESPACE" rollout status deployment/card-management-app --timeout=120s || true
    kubectl -n "$NAMESPACE" rollout status deployment/nginx --timeout=60s || true
    kubectl -n "$NAMESPACE" rollout status deployment/prometheus --timeout=60s || true
    kubectl -n "$NAMESPACE" rollout status deployment/grafana --timeout=60s || true

    echo ""
    echo "📊 Access points:"
    echo "   App (via nginx):  kubectl -n $NAMESPACE port-forward svc/nginx 443:443"
    echo "   Grafana:          kubectl -n $NAMESPACE port-forward svc/grafana 3001:3000"
    echo "   Prometheus:       kubectl -n $NAMESPACE port-forward svc/prometheus 9090:9090"
}

cmd_delete() {
    echo "🗑️  Deleting all resources in namespace: $NAMESPACE"
    kubectl delete -k "$SCRIPT_DIR" --ignore-not-found
    echo "✅ Resources deleted"
}

cmd_status() {
    echo "📋 Pods:"
    kubectl -n "$NAMESPACE" get pods -o wide
    echo ""
    echo "📋 Services:"
    kubectl -n "$NAMESPACE" get svc
    echo ""
    echo "📋 PVCs:"
    kubectl -n "$NAMESPACE" get pvc
}

case "${1:-}" in
    build)  cmd_build ;;
    apply)  cmd_apply ;;
    delete) cmd_delete ;;
    status) cmd_status ;;
    *)      usage ;;
esac
