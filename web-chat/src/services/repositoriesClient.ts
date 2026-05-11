const API_BASE = '/api';

export interface Repository {
  id: string;
  source: string;
  ownerOrOrg: string;
  name: string;
  defaultBranch: string;
  cloneUrl: string;
  agentPersona: string;
  enabled: boolean;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface CreateRepositoryRequest {
  source?: string;
  ownerOrOrg: string;
  name: string;
  defaultBranch?: string;
  cloneUrl?: string;
  agentPersona?: string;
  enabled?: boolean;
}

class RepositoriesClient {
  private baseUrl = `${API_BASE}/repositories`;

  async list(enabled?: boolean): Promise<Repository[]> {
    const url = enabled !== undefined ? `${this.baseUrl}?enabled=${enabled}` : this.baseUrl;
    const res = await fetch(url);
    if (!res.ok) throw new Error(`Failed to list repositories: ${res.statusText}`);
    return res.json();
  }

  async get(id: string): Promise<Repository> {
    const res = await fetch(`${this.baseUrl}/${id}`);
    if (!res.ok) throw new Error(`Failed to get repository: ${res.statusText}`);
    return res.json();
  }

  async create(request: CreateRepositoryRequest): Promise<Repository> {
    const res = await fetch(this.baseUrl, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(request),
    });
    if (!res.ok) throw new Error(`Failed to create repository: ${res.statusText}`);
    return res.json();
  }

  async update(id: string, request: Partial<Repository>): Promise<Repository> {
    const res = await fetch(`${this.baseUrl}/${id}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(request),
    });
    if (!res.ok) throw new Error(`Failed to update repository: ${res.statusText}`);
    return res.json();
  }

  async delete(id: string, hard = false): Promise<void> {
    const res = await fetch(`${this.baseUrl}/${id}?hard=${hard}`, { method: 'DELETE' });
    if (!res.ok) throw new Error(`Failed to delete repository: ${res.statusText}`);
  }

  async triggerIndex(id: string): Promise<{ jobId: string }> {
    const res = await fetch(`${this.baseUrl}/${id}/index`, { method: 'POST' });
    if (!res.ok) throw new Error(`Failed to trigger indexing: ${res.statusText}`);
    return res.json();
  }
}

export const repositoriesClient = new RepositoriesClient();
