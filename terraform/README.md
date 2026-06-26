# Terraform — AKS Deployment

Provisions the Card Management System on Azure Kubernetes Service (AKS) with managed PostgreSQL, Redis, and full observability stack.

## Architecture

```
Azure Resource Group
├── Virtual Network (10.0.0.0/16)
│   ├── AKS Subnet (10.0.0.0/20)
│   ├── PostgreSQL Subnet (10.0.16.0/24, delegated)
│   └── Redis Subnet (10.0.17.0/24)
├── AKS Cluster (2-5 nodes, auto-scaling)
│   ├── card-management-app (2 replicas)
│   ├── Kafka (Bitnami Helm, KRaft mode)
│   ├── WAHA (WhatsApp API)
│   ├── NGINX Ingress Controller
│   └── kube-prometheus-stack (Prometheus + Grafana)
├── Azure Database for PostgreSQL Flexible Server
├── Azure Cache for Redis
├── Azure Container Registry
└── Log Analytics Workspace (Container Insights)
```

## Prerequisites

- [Terraform](https://developer.hashicorp.com/terraform/install) >= 1.5
- [Azure CLI](https://docs.microsoft.com/en-us/cli/azure/install-azure-cli) (authenticated)
- An Azure subscription with sufficient quota

## Quick Start

```bash
# 1. Authenticate with Azure
az login
az account set --subscription "YOUR_SUBSCRIPTION_ID"

# 2. Configure variables
cp terraform.tfvars.example terraform.tfvars
# Edit terraform.tfvars with your values

# 3. Initialize Terraform
terraform init

# 4. Plan
terraform plan -out=tfplan

# 5. Apply
terraform apply tfplan

# 6. Configure kubectl
$(terraform output -raw kube_config_command)

# 7. Push your app image
az acr login --name $(terraform output -raw acr_login_server | cut -d. -f1)
docker build -t $(terraform output -raw acr_login_server)/card-management-app:latest ..
docker push $(terraform output -raw acr_login_server)/card-management-app:latest

# 8. Access Grafana
kubectl -n cardmgmt port-forward svc/prometheus-grafana 3001:80
# Open http://localhost:3001 (admin/admin, dark theme)
```

## What Gets Created

| Resource | Azure Service | Purpose |
|----------|--------------|---------|
| AKS Cluster | Azure Kubernetes Service | Container orchestration (2-5 nodes) |
| PostgreSQL | Flexible Server v16 | Application database |
| Redis | Azure Cache for Redis | API key cache, read model tracking |
| ACR | Container Registry | Docker image storage |
| VNet + Subnets | Virtual Network | Network isolation |
| Log Analytics | Monitor | Container Insights logging |
| Kafka | Bitnami Helm chart | Event streaming (in-cluster) |
| WAHA | K8s Deployment | WhatsApp messaging |
| Ingress | NGINX Ingress Controller | TLS + routing |
| Observability | kube-prometheus-stack | Prometheus + Grafana + RED/USE dashboards |

## Managed vs In-Cluster Services

| Service | docker-compose | AKS (Terraform) | Reason |
|---------|---------------|-----------------|--------|
| PostgreSQL | Container | Azure Flexible Server | HA, automated backups, patching |
| Redis | Container | Azure Cache for Redis | HA, managed TLS, persistence |
| Kafka | Container | Bitnami Helm (in-cluster) | No managed Kafka on Azure without Event Hubs |
| WAHA | Container | K8s Deployment | No managed equivalent |
| Prometheus | Container | kube-prometheus-stack Helm | Includes alerting, service monitors |
| Grafana | Container | kube-prometheus-stack Helm | Bundled with Prometheus operator |

## Cleanup

```bash
terraform destroy
```

## Remote State (recommended)

Create a storage account for Terraform state:

```bash
az group create -n tfstate-rg -l westeurope
az storage account create -n tfstatecardmgmt -g tfstate-rg -l westeurope --sku Standard_LRS
az storage container create -n tfstate --account-name tfstatecardmgmt
```

Then configure `backend.tfvars`:

```hcl
resource_group_name  = "tfstate-rg"
storage_account_name = "tfstatecardmgmt"
container_name       = "tfstate"
key                  = "cardmgmt.terraform.tfstate"
```

Initialize with: `terraform init -backend-config=backend.tfvars`
