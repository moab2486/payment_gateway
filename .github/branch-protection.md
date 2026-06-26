# Branch Protection Rules

Configure these rules in **GitHub → Settings → Branches → Branch protection rules**.

## Branch Strategy

```
feature/* ──PR──► dev ──PR──► staging ──PR──► prod
                   │              │               │
              run tests     run tests +      run tests +
              (gate PR)     docker build     deploy to AKS
```

## `dev` Branch

| Setting | Value |
|---------|-------|
| Require pull request before merging | ✅ |
| Required approvals | 1 |
| Require status checks to pass | ✅ |
| Required status checks | `Build & Test` |
| Require branches to be up to date | ✅ |
| Restrict who can push | ✅ (no direct pushes) |
| Allow force pushes | ❌ |
| Allow deletions | ❌ |

## `staging` Branch

| Setting | Value |
|---------|-------|
| Require pull request before merging | ✅ |
| Required approvals | 2 |
| Require status checks to pass | ✅ |
| Required status checks | `Validate Source Branch`, `Build, Test & Docker Image` |
| Restrict who can push | ✅ (no direct pushes) |
| Restrict PR source branches | Only `dev` |
| Allow force pushes | ❌ |
| Allow deletions | ❌ |

## `prod` Branch

| Setting | Value |
|---------|-------|
| Require pull request before merging | ✅ |
| Required approvals | 2 |
| Require review from code owners | ✅ |
| Require status checks to pass | ✅ |
| Required status checks | `Validate Source Branch`, `Full Validation` |
| Restrict who can push | ✅ (no direct pushes) |
| Restrict PR source branches | Only `staging` |
| Require conversation resolution | ✅ |
| Allow force pushes | ❌ |
| Allow deletions | ❌ |
| Require deployments to succeed | ✅ (environment: production) |

## Setup via GitHub CLI

```bash
# Dev branch protection
gh api repos/{owner}/{repo}/branches/dev/protection \
  --method PUT \
  --field required_status_checks='{"strict":true,"contexts":["Build & Test"]}' \
  --field enforce_admins=true \
  --field required_pull_request_reviews='{"required_approving_review_count":1}' \
  --field restrictions=null \
  --field allow_force_pushes=false \
  --field allow_deletions=false

# Staging branch protection
gh api repos/{owner}/{repo}/branches/staging/protection \
  --method PUT \
  --field required_status_checks='{"strict":true,"contexts":["Validate Source Branch","Build, Test & Docker Image"]}' \
  --field enforce_admins=true \
  --field required_pull_request_reviews='{"required_approving_review_count":2}' \
  --field restrictions=null \
  --field allow_force_pushes=false \
  --field allow_deletions=false

# Prod branch protection
gh api repos/{owner}/{repo}/branches/prod/protection \
  --method PUT \
  --field required_status_checks='{"strict":true,"contexts":["Validate Source Branch","Full Validation"]}' \
  --field enforce_admins=true \
  --field required_pull_request_reviews='{"required_approving_review_count":2,"require_code_owner_reviews":true}' \
  --field restrictions=null \
  --field allow_force_pushes=false \
  --field allow_deletions=false
```
