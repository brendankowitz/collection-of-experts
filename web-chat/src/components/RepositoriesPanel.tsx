import { memo, useCallback, useEffect, useState, type FormEvent } from 'react';
import { FolderGit2, Loader2, RefreshCcw, Trash2 } from 'lucide-react';
import { repositoriesClient, type CreateRepositoryRequest, type Repository } from '../services/repositoriesClient';
import { cn } from '../lib/utils';

interface RepositoriesPanelProps {
  onClose: () => void;
}

const initialForm: CreateRepositoryRequest = {
  source: 'github',
  ownerOrOrg: '',
  name: '',
  defaultBranch: 'main',
  cloneUrl: '',
  agentPersona: '',
  enabled: true,
};

const RepositoriesPanel = memo(function RepositoriesPanel({ onClose }: RepositoriesPanelProps) {
  const [repositories, setRepositories] = useState<Repository[]>([]);
  const [form, setForm] = useState<CreateRepositoryRequest>(initialForm);
  const [isLoading, setIsLoading] = useState(true);
  const [isSaving, setIsSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const loadRepositories = useCallback(async () => {
    setIsLoading(true);
    setError(null);
    try {
      const items = await repositoriesClient.list();
      setRepositories(items);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load repositories');
    } finally {
      setIsLoading(false);
    }
  }, []);

  useEffect(() => {
    void loadRepositories();
  }, [loadRepositories]);

  const handleSubmit = useCallback(async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setIsSaving(true);
    setError(null);
    try {
      await repositoriesClient.create({
        ...form,
        cloneUrl: form.cloneUrl || undefined,
        agentPersona: form.agentPersona || undefined,
      });
      setForm(initialForm);
      await loadRepositories();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to create repository');
    } finally {
      setIsSaving(false);
    }
  }, [form, loadRepositories]);

  const handleToggleEnabled = useCallback(async (repo: Repository) => {
    try {
      const updated = await repositoriesClient.update(repo.id, { enabled: !repo.enabled });
      setRepositories((current) => current.map((item) => (item.id === updated.id ? updated : item)));
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to update repository');
    }
  }, []);

  const handleTriggerIndex = useCallback(async (id: string) => {
    try {
      await repositoriesClient.triggerIndex(id);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to trigger indexing');
    }
  }, []);

  const handleDelete = useCallback(async (id: string) => {
    try {
      await repositoriesClient.delete(id);
      setRepositories((current) => current.filter((repo) => repo.id !== id));
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to delete repository');
    }
  }, []);

  return (
    <div className="flex flex-col h-full bg-slate-950 text-slate-100">
      <div className="flex items-center justify-between px-6 py-4 border-b border-slate-800">
        <div>
          <h2 className="text-lg font-semibold">Repository Registry</h2>
          <p className="text-sm text-slate-500">Add and manage expert-backed repositories.</p>
        </div>
        <div className="flex items-center gap-2">
          <button
            onClick={() => void loadRepositories()}
            className="px-3 py-2 rounded-lg bg-slate-800 hover:bg-slate-700 text-sm text-slate-200"
            type="button"
          >
            Refresh
          </button>
          <button
            onClick={onClose}
            className="px-3 py-2 rounded-lg bg-blue-600 hover:bg-blue-500 text-sm text-white"
            type="button"
          >
            Back to Chat
          </button>
        </div>
      </div>

      <div className="flex-1 overflow-y-auto p-6 grid gap-6 lg:grid-cols-[360px_minmax(0,1fr)]">
        <section className="rounded-2xl border border-slate-800 bg-slate-900/60 p-5 h-fit">
          <div className="flex items-center gap-2 mb-4">
            <FolderGit2 className="w-4 h-4 text-blue-400" />
            <h3 className="text-sm font-semibold">Add Repository</h3>
          </div>

          <form className="space-y-3" onSubmit={handleSubmit}>
            <label className="block text-xs text-slate-400">
              Owner / Org
              <input
                className="mt-1 w-full rounded-xl border border-slate-700 bg-slate-950 px-3 py-2 text-sm"
                value={form.ownerOrOrg}
                onChange={(e) => setForm((current) => ({ ...current, ownerOrOrg: e.target.value }))}
                required
              />
            </label>
            <label className="block text-xs text-slate-400">
              Repository Name
              <input
                className="mt-1 w-full rounded-xl border border-slate-700 bg-slate-950 px-3 py-2 text-sm"
                value={form.name}
                onChange={(e) => setForm((current) => ({ ...current, name: e.target.value }))}
                required
              />
            </label>
            <label className="block text-xs text-slate-400">
              Agent Persona
              <input
                className="mt-1 w-full rounded-xl border border-slate-700 bg-slate-950 px-3 py-2 text-sm"
                value={form.agentPersona ?? ''}
                onChange={(e) => setForm((current) => ({ ...current, agentPersona: e.target.value }))}
                placeholder="Optional display name"
              />
            </label>
            <label className="block text-xs text-slate-400">
              Clone URL
              <input
                className="mt-1 w-full rounded-xl border border-slate-700 bg-slate-950 px-3 py-2 text-sm"
                value={form.cloneUrl ?? ''}
                onChange={(e) => setForm((current) => ({ ...current, cloneUrl: e.target.value }))}
                placeholder="Optional; GitHub default is auto-generated"
              />
            </label>
            <button
              type="submit"
              disabled={isSaving}
              className={cn(
                'w-full rounded-xl px-3 py-2 text-sm font-medium text-white',
                isSaving ? 'bg-blue-500/60 cursor-not-allowed' : 'bg-blue-600 hover:bg-blue-500'
              )}
            >
              {isSaving ? 'Saving…' : 'Create Repository'}
            </button>
          </form>
        </section>

        <section className="rounded-2xl border border-slate-800 bg-slate-900/60 p-5 min-h-[320px]">
          <div className="flex items-center justify-between mb-4">
            <div>
              <h3 className="text-sm font-semibold">Registered Repositories</h3>
              <p className="text-xs text-slate-500 mt-1">Registry-backed expert agents update automatically.</p>
            </div>
            {isLoading && <Loader2 className="w-4 h-4 animate-spin text-slate-500" />}
          </div>

          {error && (
            <div className="mb-4 rounded-xl border border-rose-500/20 bg-rose-500/10 px-3 py-2 text-sm text-rose-300">
              {error}
            </div>
          )}

          <div className="space-y-3">
            {repositories.map((repo) => (
              <div key={repo.id} className="rounded-xl border border-slate-800 bg-slate-950/60 p-4">
                <div className="flex items-start justify-between gap-4">
                  <div>
                    <div className="flex items-center gap-2">
                      <span className="text-sm font-semibold">{repo.ownerOrOrg}/{repo.name}</span>
                      <span className={cn(
                        'rounded-full px-2 py-0.5 text-[10px] uppercase tracking-wide border',
                        repo.enabled
                          ? 'border-emerald-500/20 bg-emerald-500/10 text-emerald-300'
                          : 'border-slate-700 bg-slate-800 text-slate-400'
                      )}>
                        {repo.enabled ? 'enabled' : 'disabled'}
                      </span>
                    </div>
                    <p className="mt-1 text-xs text-slate-500">{repo.agentPersona || `${repo.name} Expert`}</p>
                    <p className="mt-2 text-xs text-slate-600">{repo.cloneUrl}</p>
                  </div>
                  <div className="flex items-center gap-2">
                    <button
                      type="button"
                      onClick={() => void handleToggleEnabled(repo)}
                      className="rounded-lg border border-slate-700 px-2.5 py-1.5 text-xs text-slate-300 hover:bg-slate-800"
                    >
                      {repo.enabled ? 'Disable' : 'Enable'}
                    </button>
                    <button
                      type="button"
                      onClick={() => void handleTriggerIndex(repo.id)}
                      className="rounded-lg border border-blue-500/20 px-2.5 py-1.5 text-xs text-blue-300 hover:bg-blue-500/10"
                    >
                      <RefreshCcw className="inline w-3 h-3 mr-1" />
                      Index
                    </button>
                    <button
                      type="button"
                      onClick={() => void handleDelete(repo.id)}
                      className="rounded-lg border border-rose-500/20 px-2.5 py-1.5 text-xs text-rose-300 hover:bg-rose-500/10"
                    >
                      <Trash2 className="inline w-3 h-3 mr-1" />
                      Remove
                    </button>
                  </div>
                </div>
              </div>
            ))}

            {!isLoading && repositories.length === 0 && (
              <div className="rounded-xl border border-dashed border-slate-800 px-4 py-10 text-center text-sm text-slate-500">
                No repositories registered yet.
              </div>
            )}
          </div>
        </section>
      </div>
    </div>
  );
});

export default RepositoriesPanel;
