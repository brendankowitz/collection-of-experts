# GitHub PR Creation & Repository Management APIs — Deep Research

## 1. Executive Summary

This research investigates the full technical stack required for AI agents to create pull requests (PRs) on behalf of users via GitHub's APIs. The scope covers REST API v3 and GraphQL v4 endpoints for branching, committing, forking, and PR creation; GitHub App authentication flows (JWT → installation tokens); the official GitHub MCP server architecture; GitHub Copilot Cloud Agent capabilities; rate limiting considerations; merge conflict handling strategies; commit signing for bot-authored commits; agent-generated PR description patterns; and critical security considerations.

**Key Findings:**
- GitHub provides a **complete REST API** for the full PR lifecycle: create branch (`POST /git/refs`), commit files (`POST /git/trees` → `POST /git/commits` → `PATCH /git/refs`), create PR (`POST /repos/{owner}/{repo}/pulls`), and manage reviews [^670^]
- **GitHub Apps** are the preferred authentication mechanism for agents, using JWT signed with RS256 to exchange for short-lived (1-hour) installation access tokens [^548^][^547^]
- The **GitHub MCP Server** (`github/github-mcp-server`) provides 50+ tools including `create_pull_request`, `create_branch`, `create_or_update_file`, `push_files`, `fork_repository`, and `merge_pull_request` [^546^]
- **GitHub Copilot Cloud Agent** is GitHub's official autonomous coding agent that can research repositories, create branches, make changes, run tests, and open PRs — all within an isolated GitHub Actions environment [^582^][^583^]
- **Rate limits** are 5,000 requests/hour for REST API and 5,000 points/hour for GraphQL API, with secondary rate limits for content creation (80 requests/minute, 500/hour) [^567^][^570^]
- **Commit signing for bots** works automatically when using the GitHub API with proper authentication — GitHub signs commits with its own GPG key when the request is verified as the GitHub App or bot [^586^][^596^]

---

## 2. Technical Architecture & Components

### 2.1 GitHub API Architecture Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                     AI AGENT LAYER                              │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────────┐  │
│  │ GitHub MCP  │  │  Octokit    │  │  Raw HTTP (fetch/axios) │  │
│  │   Server    │  │   SDKs      │  │                         │  │
│  └──────┬──────┘  └──────┬──────┘  └───────────┬─────────────┘  │
└─────────┼────────────────┼─────────────────────┼────────────────┘
          │                │                     │
          ▼                ▼                     ▼
┌─────────────────────────────────────────────────────────────────┐
│              GITHUB APP AUTHENTICATION LAYER                    │
│                                                                 │
│   ┌─────────┐    RS256 JWT    ┌─────────────┐  Installation    │
│   │ Private │  ───────────►  │ GitHub App   │  Access Token    │
│   │  Key    │                │  Auth API    │  (1 hour)        │
│   └─────────┘                └─────────────┘                  │
│                                                               │
└──────────────────────────────┬──────────────────────────────────┘
                               │
                    ┌──────────▼──────────┐
                    │   GITHUB REST API   │
                    │     & GRAPHQL       │
                    └─────────────────────┘
```

### 2.2 Core API Components for PR Creation

| Component | API | Key Endpoints | Purpose |
|-----------|-----|---------------|---------|
| **Branch Management** | REST v3 | `GET /git/ref/heads/{branch}`, `POST /git/refs` | Get branch SHA, create new branch from SHA [^573^] |
| **File Operations** | REST v3 | `PUT /contents/{path}`, `POST /git/trees`, `POST /git/blobs` | Create/update files individually or in bulk [^635^] |
| **Commit Management** | REST v3 | `POST /git/commits`, `PATCH /git/refs/{ref}` | Create commits and update branch HEAD [^654^] |
| **Pull Request** | REST v3 | `POST /repos/{o}/{r}/pulls`, `PATCH /repos/{o}/{r}/pulls/{n}` | Create and manage PRs [^670^] |
| **Forking** | REST v3 | `POST /repos/{owner}/{repo}/forks` | Fork repo to user's/org's account [^660^] |
| **Reviews** | REST v3 | `POST /repos/{o}/{r}/pulls/{n}/reviews` | Add review comments and approvals |
| **Repository** | GraphQL v4 | `createPullRequest` mutation | Alternative to REST for PR creation [^567^] |

### 2.3 MCP Server Architecture

The official **GitHub MCP Server** (`ghcr.io/github/github-mcp-server`) provides a Model Context Protocol interface over GitHub's APIs [^546^]:

**Toolsets Available:**
- `repos` — File/branch/commit operations (`get_file_contents`, `create_branch`, `create_or_update_file`, `push_files`, `fork_repository`, `list_branches`)
- `issues` — Issue management (`issue_read`, `issue_write`, `add_issue_comment`)
- `pull_requests` — PR lifecycle (`create_pull_request`, `list_pull_requests`, `merge_pull_request`, `pull_request_review_write`)
- `actions` — CI/CD workflow operations
- `code_security` — Security alerts and scanning
- `copilot` — Copilot-specific operations (`create_pull_request_with_copilot`)

**Docker deployment:**
```bash
docker run -i --rm \
  -e GITHUB_PERSONAL_ACCESS_TOKEN=<token> \
  -e GITHUB_TOOLSETS="repos,issues,pull_requests" \
  ghcr.io/github/github-mcp-server
```

---

## 3. Implementation Details

### 3.1 GitHub App Authentication (JWT → Installation Token)

GitHub Apps use a two-step authentication flow that is the **recommended approach for agents**:

**Step 1: Generate a JSON Web Token (JWT)**

The JWT must be signed using **RS256** and contain specific claims [^548^]:

```python
import time
import jwt

# Generate JWT from GitHub App private key
def generate_github_app_jwt(app_id: str, private_key_pem: str) -> str:
    payload = {
        "iat": int(time.time()) - 60,  # Issued at (60s in past for clock drift)
        "exp": int(time.time()) + 600,  # Expires at (max 10 minutes)
        "iss": app_id,  # GitHub App ID (or Client ID)
        "alg": "RS256"
    }
    return jwt.encode(payload, private_key_pem, algorithm="RS256")
```

Required JWT claims [^548^]:
| Claim | Description | Notes |
|-------|-------------|-------|
| `iat` | Issued At | Set 60s in past to protect against clock drift |
| `exp` | Expires At | Max 10 minutes into future |
| `iss` | Issuer | GitHub App's Client ID or Application ID |
| `alg` | Algorithm | Must be `RS256` |

**Step 2: Exchange JWT for Installation Access Token**

```bash
curl --request POST \
  --url "https://api.github.com/app/installations/{INSTALLATION_ID}/access_tokens" \
  --header "Accept: application/vnd.github+json" \
  --header "Authorization: Bearer {JWT}" \
  --header "X-GitHub-Api-Version: 2026-03-10" \
  --data '{"repositories":["target-repo"]}'
```

The installation token **expires after 1 hour** and can be scoped to specific repositories [^547^].

**Step 3: Use the Installation Token**

```bash
curl --request GET \
  --url "https://api.github.com/repos/{owner}/{repo}" \
  --header "Accept: application/vnd.github+json" \
  --header "Authorization: Bearer {INSTALLATION_TOKEN}"
```

### 3.2 Automatic Token Refresh with Octokit

Octokit.js handles automatic token refresh when configured with `createAppAuth` [^657^]:

```javascript
import { Octokit } from "@octokit/rest";
import { createAppAuth } from "@octokit/auth-app";

// Octokit automatically refreshes installation tokens
const installationOctokit = new Octokit({
  authStrategy: createAppAuth,
  auth: {
    appId: 123456,
    privateKey: process.env.GITHUB_APP_PRIVATE_KEY,
    installationId: 9876543,
  },
});

// Token is transparently created on first use and refreshed when expired
const { data: pr } = await installationOctokit.rest.pulls.create({
  owner: "my-org",
  repo: "my-repo",
  title: "Feature: automated change",
  head: "feature/auto-branch",
  base: "main",
  body: "This PR was created by an agent."
});
```

Key properties of this pattern [^657^]:
- Tokens are **cached** and reused until expired
- **Automatic refresh** happens transparently on the next request after expiry
- Installation ID must be provided at construction time

### 3.3 Creating a Branch via API

**Step 1: Get the base branch SHA**
```bash
curl -L \
  -H "Accept: application/vnd.github+json" \
  -H "Authorization: Bearer {TOKEN}" \
  https://api.github.com/repos/{OWNER}/{REPO}/git/ref/heads/main
# Returns: { "object": { "sha": "abc123..." } }
```

**Step 2: Create the new branch reference**
```bash
curl -L \
  -X POST \
  -H "Accept: application/vnd.github+json" \
  -H "Authorization: Bearer {TOKEN}" \
  https://api.github.com/repos/{OWNER}/{REPO}/git/refs \
  -d '{"ref": "refs/heads/feature/new-feature", "sha": "abc123..."}'
```

Response: `201 Created` on success, `422` if ref already exists [^573^].

### 3.4 Committing Files via API (Git Data API)

For **single file** changes, use the Contents API:

```bash
# Create or update a file (base64-encoded content)
curl -L -X PUT \
  -H "Accept: application/vnd.github+json" \
  -H "Authorization: Bearer {TOKEN}" \
  https://api.github.com/repos/{OWNER}/{REPO}/contents/{PATH} \
  -d '{
    "message": "Update via agent",
    "committer": {"name": "Agent Bot", "email": "agent@example.com"},
    "content": "{BASE64_CONTENT}",
    "sha": "{EXISTING_FILE_SHA}",
    "branch": "feature/new-feature"
  }'
```

For **multiple files in a single commit**, use the Git Data API [^575^][^571^]:

```javascript
const axios = require("axios");

const HEADERS = {
  Accept: "application/vnd.github.v3+json",
  Authorization: `Bearer ${TOKEN}`,
};

async function commitMultipleFiles(owner, repo, branch, files, message) {
  // 1. Get current commit SHA for branch
  const { data: { object: { sha: currentCommitSha } } } = await axios({
    url: `https://api.github.com/repos/${owner}/${repo}/git/refs/heads/${branch}`,
    headers: HEADERS,
  });

  // 2. Get current tree SHA
  const { data: { tree: { sha: treeSha } } } = await axios({
    url: `https://api.github.com/repos/${owner}/${repo}/git/commits/${currentCommitSha}`,
    headers: HEADERS,
  });

  // 3. Create new tree with file changes
  const { data: { sha: newTreeSha } } = await axios({
    url: `https://api.github.com/repos/${owner}/${repo}/git/trees`,
    method: "POST",
    headers: HEADERS,
    data: {
      base_tree: treeSha,
      tree: files.map(({ path, content }) => ({
        path,
        mode: "100644",
        type: "blob",
        content,  // UTF-8 text content
      })),
    },
  });

  // 4. Create commit
  const { data: { sha: newCommitSha } } = await axios({
    url: `https://api.github.com/repos/${owner}/${repo}/git/commits`,
    method: "POST",
    headers: HEADERS,
    data: {
      message,
      tree: newTreeSha,
      parents: [currentCommitSha],
    },
  });

  // 5. Update branch reference to point to new commit
  await axios({
    url: `https://api.github.com/repos/${owner}/${repo}/git/refs/heads/${branch}`,
    method: "PATCH",
    headers: HEADERS,
    data: { sha: newCommitSha },
  });

  return newCommitSha;
}

// Usage:
await commitMultipleFiles("my-org", "my-repo", "feature/branch", [
  { path: "src/main.js", content: "console.log('hello');" },
  { path: "README.md", content: "# Updated README" },
], "feat: automated changes by agent");
```

Tree entry modes [^635^]:
| Mode | Description |
|------|-------------|
| `100644` | Regular file (blob) |
| `100755` | Executable file (blob) |
| `040000` | Subdirectory (tree) |
| `160000` | Submodule (commit) |
| `120000` | Symlink (blob) |

### 3.5 Creating a Pull Request via REST API

```bash
curl -L -X POST \
  -H "Accept: application/vnd.github+json" \
  -H "Authorization: Bearer <TOKEN>" \
  -H "X-GitHub-Api-Version: 2026-03-10" \
  https://api.github.com/repos/{OWNER}/{REPO}/pulls \
  -d '{
    "title": "feat: add new authentication flow",
    "body": "## Summary\n\nThis PR adds OAuth2 authentication.\n\n## Changes\n- Added login endpoint\n- Added token refresh\n- Added tests\n\n## Testing\n- [x] Unit tests pass\n- [x] Integration tests pass",
    "head": "feature/oauth-flow",
    "base": "main",
    "draft": false,
    "maintainer_can_modify": true
  }'
```

**Parameters** [^670^]:
| Parameter | Required | Description |
|-----------|----------|-------------|
| `title` | Yes | PR title |
| `head` | Yes | Branch containing changes (format: `branch-name` or `user:branch-name` for cross-repo) |
| `base` | Yes | Branch to merge into |
| `body` | No | PR description (markdown) |
| `draft` | No | Create as draft PR |
| `maintainer_can_modify` | No | Allow maintainers to edit |

**Cross-repository PRs** (from a fork): Set `head` to `fork-owner:branch-name`.

### 3.6 Forking a Repository

```bash
curl -L -X POST \
  -H "Accept: application/vnd.github+json" \
  -H "Authorization: Bearer <TOKEN>" \
  https://api.github.com/repos/{UPSTREAM_OWNER}/{REPO}/forks \
  -d '{"organization": "my-org", "name": "my-fork", "default_branch_only": false}'
```

After forking, the agent works in the forked repository and creates a PR back to the upstream [^660^].

### 3.7 Complete End-to-End Flow

```typescript
// Complete agent workflow using Octokit
import { Octokit } from "@octokit/rest";
import { createAppAuth } from "@octokit/auth-app";

class GitHubAgent {
  private octokit: Octokit;

  constructor(appId: number, privateKey: string, installationId: number) {
    this.octokit = new Octokit({
      authStrategy: createAppAuth,
      auth: { appId, privateKey, installationId },
    });
  }

  async createFeaturePR(
    owner: string,
    repo: string,
    baseBranch: string,
    featureBranch: string,
    files: Array<{ path: string; content: string }>,
    title: string,
    description: string
  ) {
    // 1. Get base branch SHA
    const { data: baseRef } = await this.octokit.git.getRef({
      owner, repo, ref: `heads/${baseBranch}`,
    });
    const baseSha = baseRef.object.sha;

    // 2. Create feature branch
    await this.octokit.git.createRef({
      owner, repo, ref: `refs/heads/${featureBranch}`, sha: baseSha,
    });

    // 3. Create blobs for each file
    const treeEntries = [];
    for (const file of files) {
      treeEntries.push({
        path: file.path,
        mode: "100644" as const,
        type: "blob" as const,
        content: file.content,
      });
    }

    // 4. Get current tree
    const { data: baseCommit } = await this.octokit.git.getCommit({
      owner, repo, commit_sha: baseSha,
    });

    // 5. Create new tree
    const { data: newTree } = await this.octokit.git.createTree({
      owner, repo, base_tree: baseCommit.tree.sha, tree: treeEntries,
    });

    // 6. Create commit
    const { data: newCommit } = await this.octokit.git.createCommit({
      owner, repo, message: `feat: ${title}`, tree: newTree.sha,
      parents: [baseSha],
    });

    // 7. Update branch reference
    await this.octokit.git.updateRef({
      owner, repo, ref: `heads/${featureBranch}`, sha: newCommit.sha,
    });

    // 8. Create pull request
    const { data: pr } = await this.octokit.rest.pulls.create({
      owner, repo, title, body: description,
      head: featureBranch, base: baseBranch, draft: false,
    });

    return pr;
  }
}
```

---

## 4. Configuration & Setup

### 4.1 GitHub App Registration

To create a GitHub App for an agent:

1. Go to **Settings → Developer settings → GitHub Apps → New GitHub App**
2. Set **GitHub App name**, **Description**, **Homepage URL**
3. Disable **Webhook** (unless needed)
4. Set **Permissions** (see table below)
5. Set **Repository access** to "Only on this account" or organization-wide
6. Generate and download **Private key** (.pem file)

### 4.2 Required GitHub App Permissions

For a PR-creating agent, the GitHub App needs these permissions [^669^][^632^]:

| Permission | Access | Purpose |
|------------|--------|---------|
| **Metadata** | Read-only | Required for all GitHub Apps (repository info, collaborators) |
| **Contents** | Read & write | Read files, create branches, create commits |
| **Issues** | Read & write | Read issues (for context), add comments |
| **Pull requests** | Read & write | Create PRs, update PRs, read PR data |
| **Actions** | Read (optional) | Check workflow run status |
| **Checks** | Read (optional) | Check CI status |
| **Commit statuses** | Read (optional) | Check build status |
| **Workflows** | Write (if needed) | Update GitHub Actions workflow files |

### 4.3 Fine-Grained Personal Access Token (Alternative)

For scenarios where a GitHub App is not suitable, fine-grained PATs provide scoped access [^612^][^614^]:

- Limited to **resources owned by a single user or organization**
- Can be limited to **specific repositories**
- **Granular permissions** (Contents:write, Pull requests:write, etc.)
- Supports expiration dates
- **Limitations**: Cannot access multiple orgs, cannot access Checks API fully, cannot access Packages [^612^]

### 4.4 GitHub MCP Server Configuration

**Environment variables** [^546^]:
```bash
# Authentication (one of)
GITHUB_PERSONAL_ACCESS_TOKEN=<pat>       # Fine-grained or classic PAT
# Or use GitHub CLI auth: gh auth token

# Toolset filtering
GITHUB_TOOLSETS="repos,issues,pull_requests,code_security,actions"

# Or individual tools
GITHUB_TOOLS="create_pull_request,create_branch,create_or_update_file"

# Read-only mode (disables all write operations)
GITHUB_READONLY=true

# Docker deployment
docker run -i --rm \
  -e GITHUB_PERSONAL_ACCESS_TOKEN=<token> \
  -e GITHUB_TOOLSETS="repos,issues,pull_requests" \
  ghcr.io/github/github-mcp-server
```

---

## 5. Integration Patterns

### 5.1 GitHub Copilot Cloud Agent (Official Reference)

GitHub Copilot Cloud Agent is the canonical example of an autonomous PR-creating agent [^582^][^583^]:

**Workflow:**
1. **Assignment** — User assigns a GitHub issue to `@copilot` or delegates from chat
2. **Analysis** — Agent analyzes the task and repository structure
3. **Development** — Works in an isolated GitHub Actions environment, explores codebase, makes changes, runs tests
4. **PR Creation** — Creates a PR with implementation details
5. **Iteration** — Responds to `@copilot` mentions in PR comments

**Key capabilities** [^583^]:
- Research a repository and create implementation plans
- Fix bugs and implement incremental features
- Resolve merge conflicts automatically
- Improve test coverage and update documentation
- Address technical debt

**Security model** [^636^][^609^]:
- Only responds to users with **write access** to the repository
- Can only push to a **single branch** (`copilot/` prefix or existing PR branch)
- **Cannot push to default branch** (e.g., `main`)
- **Cannot access GitHub Actions secrets** (only Agent secrets/variables)
- Commits are **signed and verified** as authored by Copilot
- Each commit links to **session logs** for auditability
- Draft PRs require human review before workflows run

**MCP integration**: Copilot Cloud Agent supports MCP servers for external tools [^663^][^665^]:
```json
{
  "mcpServers": {
    "my-external-api": {
      "type": "local",
      "command": "npx",
      "args": ["-y", "@my-org/mcp-server"],
      "tools": ["search_docs", "get_schema"]
    }
  }
}
```

### 5.2 Pattern: Agent-Generated PR Descriptions

The **PR-Agent** project (Qodo, formerly CodiumAI) provides a reference for automated PR description generation [^613^][^615^]:

**Template structure:**
```markdown
## PR Type
- [ ] Bug fix
- [ ] Feature
- [ ] Refactor
- [ ] Documentation

## Description
{AI-generated summary of changes}

## Changes Walkthrough
| File | Change Summary |
|------|---------------|
| `src/auth.js` | Added OAuth2 flow |
| `tests/auth.test.js` | Added unit tests |

## Testing
{List of test results}

## Possible Issues
{AI-detected potential issues}
```

**Best practices for agent PRs** [^592^]:
- Include **type labels** automatically (`feature:`, `fix:`, `docs:`)
- Provide **file-by-file change walkthrough**
- List **potential issues** and security concerns
- Include **testing checklist** with automated status
- Reference **related issues** via `Fixes #123`

### 5.3 Pattern: Handling Cross-Repository PRs (Fork-based)

When the agent doesn't have write access to the target repository:

```javascript
// 1. Fork the repository
const { data: fork } = await octokit.repos.createFork({
  owner: "upstream-org", repo: "target-repo",
});

// Wait for fork to be ready (async operation)
await new Promise(r => setTimeout(r, 5000));

// 2. Work in the fork
const forkOwner = fork.owner.login;

// Create branch, commit, push in fork
// ... (same as section 3.7 but targeting fork)

// 3. Create PR from fork to upstream
const { data: pr } = await octokit.rest.pulls.create({
  owner: "upstream-org",      // Target repo owner
  repo: "target-repo",        // Target repo
  title: "feat: new feature",
  head: `${forkOwner}:feature-branch`,  // Note: user:branch format
  base: "main",
  body: "PR description...",
});
```

---

## 6. Limitations & Gotchas

### 6.1 Rate Limits

**Primary Rate Limits** [^570^][^567^]:

| Authentication Type | REST API | GraphQL API |
|---------------------|----------|-------------|
| Unauthenticated | 60 req/hour | N/A (auth required) |
| Personal Access Token | 5,000 req/hour | 5,000 points/hour |
| GitHub App (normal org) | 5,000 req/hour | 5,000 points/hour |
| GitHub App (GHEC org) | 15,000 req/hour | 10,000 points/hour |
| GITHUB_TOKEN in Actions | 1,000 req/hour (per repo) | 1,000 points/hour |

**Secondary Rate Limits** [^567^]:
- Max **100 concurrent requests** (shared REST + GraphQL)
- Max **900 points/minute** for REST API endpoints
- Max **2,000 points/minute** for GraphQL API
- Max **90 seconds CPU time per 60 seconds real time**
- Max **80 content-generating requests/minute** (500/hour)
- Content creation includes PR creation, issue creation, comments, commits

**Rate limit headers** in every response:
```
X-RateLimit-Limit: 5000
X-RateLimit-Remaining: 4999
X-RateLimit-Used: 1
X-RateLimit-Reset: 1691591363
```

### 6.2 Branch Protection Rules

Agents may be blocked by branch protection rules [^642^][^583^]:

| Protection Setting | Impact on Agents |
|-------------------|-----------------|
| **Require signed commits** | Bot commits via API are auto-signed when auth is proper [^586^] |
| **Require PR reviews** | Agent cannot bypass — requires human approval |
| **Require status checks** | May block auto-merge — need bypass permissions |
| **Restrict push actors** | Must add GitHub App to allowed actors list |
| **Require linear history** | Agent must use rebase, not merge commits |

**Mitigation**: Use **rulesets** and add the GitHub App as a **bypass actor** [^583^].

### 6.3 Merge Conflict Handling

**Strategies for agents** [^585^][^587^]:

1. **Prevention**: Keep agent branches short-lived; rebase frequently before creating PR
2. **Detection**: After pushing, check PR for `mergeable_state: "dirty"` via API
3. **Resolution options**:
   - **GitHub-native**: API has `Update branch` button equivalent (`PUT /repos/{o}/{r}/pulls/{n}/update-branch`)
   - **Rebase approach**: Agent fetches latest base, rebases its branch, force-pushes
   - **AI-assisted resolution**: Parse conflict markers (`<<<<<<<`, `=======`, `>>>>>>>`) and use LLM to resolve [^587^]

```javascript
// Check mergeability
const { data: pr } = await octokit.rest.pulls.get({
  owner, repo, pull_number: prNumber,
});
if (pr.mergeable === false) {
  // Conflict detected — notify user or attempt resolution
}
```

### 6.4 Commit Signing for Bots

**Key finding**: When using the GitHub API with proper GitHub App authentication, GitHub automatically signs commits with its own GPG key [^586^][^596^]:

> "Signature verification for bots will only work if the request is verified and authenticated as the GitHub App or bot and contains no custom author information, custom committer information, and no custom signature information."

**For GitHub Actions**: The `GITHUB_TOKEN` produces unsigned commits by default. Use the **REST API** (not local `git commit`) to get verified commits [^594^]:

```bash
# This creates a VERIFIED commit (signed by GitHub)
curl -X POST \
  -H "Authorization: Bearer ${INSTALLATION_TOKEN}" \
  https://api.github.com/repos/{owner}/{repo}/git/commits \
  -d '{"message": "auto-generated", "tree": "{TREE_SHA}", "parents": ["{PARENT_SHA}"]}'

# Then update the ref
curl -X PATCH \
  -H "Authorization: Bearer ${INSTALLATION_TOKEN}" \
  https://api.github.com/repos/{owner}/{repo}/git/refs/heads/{branch} \
  -d '{"sha": "{NEW_COMMIT_SHA}"}'
```

Commits created this way appear as **"Verified"** on GitHub because they are signed by GitHub's own GPG key [^594^][^596^].

### 6.5 Content API vs Git Data API

| Approach | Best For | Limitations |
|----------|----------|-------------|
| **Contents API** (`PUT /contents/{path}`) | Single file changes | One file per request; simpler |
| **Git Data API** (`/git/trees`, `/git/commits`) | Multiple file commits | More complex; requires tree→commit→ref update chain |
| **MCP `push_files`** | Multiple files, one commit | Abstracts the Git Data API complexity |

---

## 7. Recommendations for Prototype

### 7.1 Recommended Architecture

```
┌─────────────────────────────────────────────────────────┐
│                    YOUR AI AGENT                        │
│  ┌──────────┐  ┌──────────┐  ┌──────────────────────┐  │
│  │ Planner  │  │  Coder   │  │   PR Orchestrator    │  │
│  └────┬─────┘  └────┬─────┘  └──────────┬───────────┘  │
│       │              │                   │               │
│       └──────────────┴───────────────────┘               │
│                          │                               │
│                          ▼                               │
│              ┌─────────────────────┐                     │
│              │  GitHub MCP Server  │                     │
│              │  (tool interface)   │                     │
│              └─────────────────────┘                     │
└──────────────────────────┬──────────────────────────────┘
                           │
              ┌────────────┴────────────┐
              ▼                         ▼
    ┌──────────────────┐    ┌──────────────────┐
    │   Octokit.js     │    │  GitHub App Auth │
    │   (SDK wrapper)  │    │  (JWT → Token)   │
    └────────┬─────────┘    └────────┬─────────┘
             │                       │
             └───────────┬───────────┘
                         ▼
              ┌─────────────────────┐
              │   GitHub REST API   │
              │    (api.github.com) │
              └─────────────────────┘
```

### 7.2 Recommended Tech Stack

| Component | Recommendation | Reason |
|-----------|---------------|--------|
| **Authentication** | GitHub App (not PAT) | Better security, scoped per-installation, auto-refresh tokens |
| **SDK** | Octokit.js (`@octokit/rest`) | Automatic token refresh, typed API, battle-tested |
| **Interface** | GitHub MCP Server | Provides 50+ tools, handles auth, consistent interface |
| **Deployment** | Docker container | Portable, environment-variable based config |
| **PR Descriptions** | Structured markdown template | Consistency, includes file walkthrough and testing checklist |

### 7.3 Recommended Workflow for Agent PR Creation

```
1. RECEIVE TASK (e.g., "fix bug #123")
   │
   ▼
2. AUTHENTICATE (GitHub App → Installation Token)
   │
   ▼
3. GATHER CONTEXT
   ├── Read issue #123
   ├── Read relevant files via MCP `get_file_contents`
   └── Read repository structure
   │
   ▼
4. PLAN CHANGES
   ├── Identify files to modify
   ├── Draft implementation
   └── Create branch via `create_branch` (e.g., `agent/fix-bug-123`)
   │
   ▼
5. IMPLEMENT CHANGES
   ├── Create/update files via `create_or_update_file` or `push_files`
   └── Run tests (if CI configured)
   │
   ▼
6. CREATE PR via `create_pull_request`
   ├── Title: conventional commit format ("fix: resolve null pointer in auth")
   ├── Body: structured template with summary, changes, testing checklist
   └── Set draft=true initially, then mark ready after verification
   │
   ▼
7. MONITOR & ITERATE
   ├── Watch for CI status via `list_workflow_runs`
   ├── Respond to review comments if needed
   └── Mark ready for review when checks pass
```

### 7.4 Security Checklist for Production

- [ ] Register as **GitHub App** (not OAuth app or PAT)
- [ ] Request **minimum permissions** (Contents:write, Pull requests:write, Issues:read)
- [ ] Store **private key** securely (AWS Secrets Manager, Azure Key Vault, etc.)
- [ ] Generate **short-lived tokens** (1 hour max, automatic refresh)
- [ ] Add branch protection **bypass rules** only if necessary, scoped to specific apps
- [ ] Use **draft PRs** initially, require human review before merge
- [ ] **Never** grant access to GitHub Actions secrets from agent context
- [ ] **Log all actions** for auditability (GitHub Copilot links commits to session logs)
- [ ] Implement **rate limit monitoring** with exponential backoff
- [ ] Set **max concurrent requests** below 100 to avoid secondary rate limits

---

## 8. Sources & References

### Official Documentation
1. [^670^] GitHub REST API — Pull Requests: https://docs.github.com/en/rest/pulls/pulls
2. [^548^] Generating a JWT for a GitHub App: https://docs.github.com/en/apps/creating-github-apps/authenticating-with-a-github-app/generating-a-json-web-token-jwt-for-a-github-app
3. [^547^] Authenticating as a GitHub App Installation: https://docs.github.com/en/apps/creating-github-apps/authenticating-with-a-github-app/authenticating-as-a-github-app-installation
4. [^549^] Generating an Installation Access Token: https://docs.github.com/en/apps/creating-github-apps/authenticating-with-a-github-app/generating-an-installation-access-token-for-a-github-app
5. [^570^] Rate Limits for the REST API: https://docs.github.com/en/rest/using-the-rest-api/rate-limits-for-the-rest-api
6. [^567^] Rate Limits for the GraphQL API: https://docs.github.com/en/graphql/overview/rate-limits-and-query-limits-for-the-graphql-api
7. [^586^] About Commit Signature Verification: https://docs.github.com/en/authentication/managing-commit-signature-verification/about-commit-signature-verification
8. [^566^] Signing Commits: https://docs.github.com/en/authentication/managing-commit-signature-verification/signing-commits
9. [^573^] REST API for Git References: https://docs.github.com/rest/git/refs
10. [^635^] REST API for Git Trees: https://docs.github.com/en/rest/git/trees
11. [^612^] Managing Personal Access Tokens: https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/managing-your-personal-access-tokens
12. [^614^] Permissions for Fine-Grained PATs: https://docs.github.com/en/rest/authentication/permissions-required-for-fine-grained-personal-access-tokens
13. [^669^] Permissions Required for GitHub Apps: https://docs.github.com/en/rest/authentication/permissions-required-for-github-apps
14. [^642^] About Protected Branches: https://docs.github.com/repositories/configuring-branches-and-merges-in-your-repository/managing-protected-branches/about-protected-branches
15. [^585^] Resolving a Merge Conflict on GitHub: https://docs.github.com/articles/resolving-a-merge-conflict-on-github

### GitHub MCP Server & Copilot
16. [^546^] GitHub MCP Server (Official): https://github.com/github/github-mcp-server
17. [^582^] GitHub Copilot Cloud Agent: https://code.visualstudio.com/docs/copilot/copilot-cloud-agent
18. [^583^] About GitHub Copilot Cloud Agent: https://docs.github.com/copilot/concepts/agents/coding-agent/about-coding-agent
19. [^609^] Responsible Use of Copilot Cloud Agent: https://docs.github.com/en/copilot/responsible-use/copilot-cloud-agent
20. [^636^] Risks and Mitigations for Copilot Cloud Agent: https://docs.github.com/en/copilot/concepts/agents/cloud-agent/risks-and-mitigations
21. [^639^] Customizing Firewall for Copilot Cloud Agent: https://docs.github.com/copilot/customizing-copilot/customizing-or-disabling-the-firewall-for-copilot-coding-agent
22. [^663^] MCP and GitHub Copilot Cloud Agent: https://docs.github.com/en/copilot/concepts/agents/cloud-agent/mcp-and-cloud-agent
23. [^665^] Connect Agents to External Tools (MCP): https://docs.github.com/en/copilot/how-tos/copilot-on-github/customize-copilot/customize-cloud-agent/extend-cloud-agent-with-mcp
24. [^664^] About Model Context Protocol (MCP): https://docs.github.com/en/copilot/concepts/context/mcp

### SDKs and Libraries
25. [^595^] Octokit.js REST API Library: https://octokit.github.io/rest.js/
26. [^657^] Octokit Auth App.js (automatic token refresh): https://github.com/octokit/auth-app.js/
27. [^650^] Octokit auto-refresh implementation: https://github.com/octokit/auth-app.js/issues/517
28. [^653^] GitHub App Token Refresh Example: https://github.com/cvega/githubapp-token-refresh

### Code Examples & Patterns
29. [^575^] Commit Multiple Files via REST API (Gist): https://gist.github.com/quilicicf/41e241768ab8eeec1529869777e996f0
30. [^571^] Push Multiple Files in Single Commit (Community): https://github.com/orgs/community/discussions/166611
31. [^565^] Create a Branch via API (Python): https://gist.github.com/ursulacj/36ade01fa6bd5011ea31f3f6b572834e
32. [^654^] How to Programmatically Create a Commit: https://blog.apihero.run/how-to-programmatically-create-a-commit-on-github
33. [^647^] Upload Files via GitHub API: https://github.com/orgs/community/discussions/83252
34. [^660^] Fork a Repo via GitHub API: https://superuser.com/questions/1208835/fork-a-repo-via-github-api-by-curl
35. [^668^] Create PR with GitHub Actions: https://www.baeldung.com/ops/github-actions-create-pr

### Security & Signing
36. [^594^] Sign Commits using GitHub Actions App: https://192dot.medium.com/sign-commit-using-github-actions-app-13488f6e76b7
37. [^596^] Commit Signing with GitHub Apps: https://github.com/orgs/community/discussions/50055
38. [^611^] Securing GitHub Copilot in GitHub Actions: https://www.stepsecurity.io/blog/securing-github-copilot-in-github-actions-with-harden-runner

### PR Automation & Patterns
39. [^613^] PR-Agent (Qodo, open-source): https://github.com/Writesonic/qodo-pr-agent
40. [^615^] PR-Agent Overview: https://aiagentstore.ai/ai-agent/pr-agent
41. [^592^] Automating PR Workflows with PR-Agent: https://www.metacto.com/blogs/automating-pull-request-workflows-with-pr-agent

### Rate Limits & Best Practices
42. [^572^] Understanding GitHub API Rate Limits: https://github.com/orgs/community/discussions/163553
43. [^577^] Finding & Fixing GitHub API Rate-Limit Issues: https://medium.com/@rahul.fiem/finding-fixing-a-github-api-rate-limit-culprit-a780210a0d0e

### Merge Conflicts
44. [^587^] AI-Powered Merge Conflict Resolution (3-tier strategy): https://github.com/akaszubski/autonomous-dev/issues/183
45. [^588^] GitHub Actions and Merge Conflicts: https://medium.com/@FartsyRainbowOctopus/github-actions-and-merge-conflicts-a-comprehensive-analysis

---

*Research conducted: June 2025*
*Total independent searches: 17*
*Sources consulted: 45+*
