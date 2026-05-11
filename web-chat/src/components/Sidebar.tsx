import { memo, useCallback } from 'react';
import {
  Plus,
  MessageSquare,
  Database,
  Activity,
  Settings,
  ChevronLeft,
  X,
  FolderGit2,
} from 'lucide-react';
import { useChatStore } from '../store/chatStore';
import { cn } from '../lib/utils';

const iconMap = {
  database: Database,
  activity: Activity,
  settings: Settings,
};

const statusConfig = {
  online: { color: 'bg-emerald-500', label: 'Online' },
  offline: { color: 'bg-slate-500', label: 'Offline' },
  busy: { color: 'bg-amber-500', label: 'Busy' },
};

const colorConfig: Record<string, { bg: string; border: string; text: string; dot: string }> = {
  fhir: {
    bg: 'bg-emerald-500/10',
    border: 'border-emerald-500/20',
    text: 'text-emerald-400',
    dot: 'bg-emerald-500',
  },
  healthcare: {
    bg: 'bg-blue-500/10',
    border: 'border-blue-500/20',
    text: 'text-blue-400',
    dot: 'bg-blue-500',
  },
  system: {
    bg: 'bg-slate-500/10',
    border: 'border-slate-500/20',
    text: 'text-slate-400',
    dot: 'bg-slate-500',
  },
};

interface SidebarProps {
  onManageRepositories: () => void;
  onShowChat: () => void;
  isRepositoriesOpen: boolean;
}

const Sidebar = memo(function Sidebar({ onManageRepositories, onShowChat, isRepositoriesOpen }: SidebarProps) {
  const agents = useChatStore((s) => s.agents);
  const selectedAgent = useChatStore((s) => s.selectedAgent);
  const isSidebarOpen = useChatStore((s) => s.isSidebarOpen);
  const selectAgent = useChatStore((s) => s.selectAgent);
  const clearMessages = useChatStore((s) => s.clearMessages);
  const toggleSidebar = useChatStore((s) => s.toggleSidebar);

  const handleNewChat = useCallback(() => {
    onShowChat();
    clearMessages();
  }, [clearMessages, onShowChat]);

  const handleSelectAgent = useCallback(
    (id: string) => {
      if (id !== 'system') {
        onShowChat();
        selectAgent(id);
      }
    },
    [onShowChat, selectAgent]
  );

  const chatAgents = agents.filter((a) => a.id !== 'system');

  return (
    <>
      {isSidebarOpen && (
        <div
          className="fixed inset-0 bg-black/50 backdrop-blur-sm z-40 md:hidden"
          onClick={toggleSidebar}
          aria-hidden="true"
        />
      )}

      <aside
        className={cn(
          'fixed md:relative z-50 flex flex-col h-full w-80 md:w-72 lg:w-80',
          'bg-slate-900/95 backdrop-blur-xl border-r border-slate-800',
          'transition-transform duration-300 ease-smooth',
          'md:translate-x-0',
          isSidebarOpen ? 'translate-x-0' : '-translate-x-full'
        )}
        aria-label="Agent sidebar"
      >
        <div className="flex items-center justify-between px-4 py-3 border-b border-slate-800">
          <div className="flex items-center gap-2.5">
            <div className="w-8 h-8 rounded-lg bg-gradient-to-br from-blue-500 to-indigo-600 flex items-center justify-center shadow-lg shadow-blue-500/20">
              <MessageSquare className="w-4 h-4 text-white" />
            </div>
            <div>
              <h1 className="text-sm font-semibold text-slate-200">Expert Agents</h1>
              <p className="text-[10px] text-slate-500">AI-Powered Chat</p>
            </div>
          </div>
          <button
            onClick={toggleSidebar}
            className="md:hidden p-1.5 rounded-lg hover:bg-slate-800 text-slate-400 transition-colors"
            aria-label="Close sidebar"
          >
            <X className="w-4 h-4" />
          </button>
        </div>

        <div className="px-3 py-3">
          <button
            onClick={handleNewChat}
            className={cn(
              'w-full flex items-center gap-2.5 px-3 py-2.5 rounded-xl',
              'bg-slate-800 hover:bg-slate-700/80 border border-slate-700/50',
              'text-sm font-medium text-slate-200',
              'transition-all duration-200 hover:shadow-lg hover:shadow-black/20',
              'active:scale-[0.98]'
            )}
          >
            <Plus className="w-4 h-4" />
            New Chat
          </button>
        </div>

        <div className="flex-1 overflow-y-auto px-3 pb-4">
          <div className="mb-2 px-1">
            <span className="text-[10px] font-semibold text-slate-500 uppercase tracking-wider">
              Available Agents
            </span>
          </div>

          <div className="space-y-1.5" role="list">
            {chatAgents.map((agent) => {
              const Icon = iconMap[agent.icon as keyof typeof iconMap] || MessageSquare;
              const isActive = !isRepositoriesOpen && selectedAgent === agent.id;
              const colors = colorConfig[agent.color] || colorConfig.system;
              const status = statusConfig[agent.status];

              return (
                <button
                  key={agent.id}
                  onClick={() => handleSelectAgent(agent.id)}
                  role="listitem"
                  className={cn(
                    'w-full flex items-start gap-3 p-3 rounded-xl text-left',
                    'transition-all duration-200 border',
                    isActive
                      ? `${colors.bg} ${colors.border} shadow-sm`
                      : 'bg-transparent border-transparent hover:bg-slate-800/60 hover:border-slate-700/30'
                  )}
                  aria-label={`${agent.name} - ${status.label}`}
                  aria-selected={isActive}
                >
                  <div
                    className={cn(
                      'flex-shrink-0 w-10 h-10 rounded-xl flex items-center justify-center',
                      'border transition-colors',
                      isActive
                        ? `${colors.bg} ${colors.border} ${colors.text}`
                        : 'bg-slate-800/50 border-slate-700/50 text-slate-400'
                    )}
                  >
                    <Icon className="w-5 h-5" />
                  </div>

                  <div className="flex-1 min-w-0">
                    <div className="flex items-center gap-2">
                      <span
                        className={cn(
                          'text-sm font-medium truncate',
                          isActive ? 'text-slate-100' : 'text-slate-300'
                        )}
                      >
                        {agent.name}
                      </span>
                      <span
                        className={cn(
                          'w-2 h-2 rounded-full border border-slate-900 flex-shrink-0',
                          status.color
                        )}
                        title={status.label}
                      />
                    </div>
                    <p className="text-xs text-slate-500 mt-0.5 line-clamp-2 leading-relaxed">
                      {agent.description}
                    </p>
                  </div>

                  {isActive && (
                    <ChevronLeft className="w-3.5 h-3.5 text-slate-500 flex-shrink-0 mt-1" />
                  )}
                </button>
              );
            })}
          </div>

          <div className="mt-6 px-3 py-3 rounded-xl bg-slate-800/30 border border-slate-800/50">
            <span className="text-[10px] font-semibold text-slate-600 uppercase tracking-wider">
              Session
            </span>
            <p className="text-[10px] text-slate-500 mt-1 font-mono break-all">
              {useChatStore.getState().sessionId}
            </p>
          </div>
        </div>

        <div className="px-4 py-3 border-t border-slate-800 space-y-2">
          <button
            onClick={onManageRepositories}
            className={cn(
              'w-full flex items-center justify-center gap-2 rounded-xl border px-3 py-2 text-sm transition-colors',
              isRepositoriesOpen
                ? 'border-blue-500/30 bg-blue-500/10 text-blue-300'
                : 'border-slate-700 bg-slate-800/60 text-slate-300 hover:bg-slate-800'
            )}
            type="button"
          >
            <FolderGit2 className="w-4 h-4" />
            Manage Repos
          </button>
          <p className="text-[10px] text-slate-600 text-center">
            Microsoft Healthcare • FHIR Server • v1.0.0
          </p>
        </div>
      </aside>
    </>
  );
});

export default Sidebar;
