import { useState, useEffect, useCallback, useRef, memo } from 'react';
import { useChatStore, type Agent } from '../store/chatStore';

interface AgentMentionProps {
  text: string;
  cursorPosition: number;
  onSelect: (agent: Agent) => void;
  onClose: () => void;
}

const AgentMention = memo(function AgentMention({
  text,
  cursorPosition,
  onSelect,
  onClose,
}: AgentMentionProps) {
  const agents = useChatStore((s) => s.agents);
  const [filteredAgents, setFilteredAgents] = useState<Agent[]>([]);
  const [selectedIndex, setSelectedIndex] = useState(0);
  const [query, setQuery] = useState('');
  const listRef = useRef<HTMLDivElement>(null);
  const itemRefs = useRef<(HTMLButtonElement | null)[]>([]);

  // Filter agents based on query after @
  useEffect(() => {
    // Find the position of @ before the cursor
    const textBeforeCursor = text.slice(0, cursorPosition);
    const lastAtIndex = textBeforeCursor.lastIndexOf('@');

    if (lastAtIndex === -1) {
      onClose();
      return;
    }

    // Check if there's a space between @ and cursor (which would mean we exited the mention)
    const textAfterAt = textBeforeCursor.slice(lastAtIndex + 1);
    if (textAfterAt.includes(' ') && textAfterAt.trim() !== '') {
      onClose();
      return;
    }

    const searchQuery = textAfterAt.toLowerCase();
    setQuery(searchQuery);

    const filtered = agents.filter(
      (a) =>
        a.id !== 'system' &&
        (a.name.toLowerCase().includes(searchQuery) ||
          a.id.toLowerCase().includes(searchQuery))
    );

    setFilteredAgents(filtered);
    setSelectedIndex(0);

    if (filtered.length === 0) {
      onClose();
    }
  }, [text, cursorPosition, agents, onClose]);

  // Keyboard navigation
  const handleKeyDown = useCallback(
    (e: KeyboardEvent) => {
      if (filteredAgents.length === 0) return;

      switch (e.key) {
        case 'ArrowDown':
          e.preventDefault();
          setSelectedIndex((prev) =>
            prev < filteredAgents.length - 1 ? prev + 1 : 0
          );
          break;
        case 'ArrowUp':
          e.preventDefault();
          setSelectedIndex((prev) =>
            prev > 0 ? prev - 1 : filteredAgents.length - 1
          );
          break;
        case 'Enter':
        case 'Tab':
          e.preventDefault();
          if (filteredAgents[selectedIndex]) {
            onSelect(filteredAgents[selectedIndex]);
          }
          break;
        case 'Escape':
          e.preventDefault();
          onClose();
          break;
      }
    },
    [filteredAgents, selectedIndex, onSelect, onClose]
  );

  useEffect(() => {
    document.addEventListener('keydown', handleKeyDown);
    return () => document.removeEventListener('keydown', handleKeyDown);
  }, [handleKeyDown]);

  // Scroll selected item into view
  useEffect(() => {
    const el = itemRefs.current[selectedIndex];
    if (el) {
      el.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
    }
  }, [selectedIndex]);

  // Click outside to close
  useEffect(() => {
    const handleClick = (e: MouseEvent) => {
      if (listRef.current && !listRef.current.contains(e.target as Node)) {
        onClose();
      }
    };
    document.addEventListener('mousedown', handleClick);
    return () => document.removeEventListener('mousedown', handleClick);
  }, [onClose]);

  if (filteredAgents.length === 0) return null;

  const getAgentColor = (color: string) => {
    switch (color) {
      case 'fhir':
        return 'bg-emerald-500/10 text-emerald-400 border-emerald-500/20';
      case 'healthcare':
        return 'bg-blue-500/10 text-blue-400 border-blue-500/20';
      default:
        return 'bg-slate-500/10 text-slate-400 border-slate-500/20';
    }
  };

  const getStatusDot = (status: Agent['status']) => {
    switch (status) {
      case 'online':
        return 'bg-emerald-500';
      case 'offline':
        return 'bg-slate-500';
      case 'busy':
        return 'bg-amber-500';
    }
  };

  return (
    <div
      ref={listRef}
      className="absolute bottom-full left-0 mb-2 w-80 max-w-[90vw] bg-slate-900 border border-slate-700 rounded-xl shadow-2xl shadow-black/30 overflow-hidden z-50"
      role="listbox"
      aria-label="Select an agent"
    >
      <div className="px-3 py-2 bg-slate-800/50 border-b border-slate-700/50">
        <span className="text-xs font-medium text-slate-400 uppercase tracking-wider">
          {query ? `Agents matching "${query}"` : 'Available Agents'}
        </span>
      </div>
      <div className="max-h-60 overflow-y-auto">
        {filteredAgents.map((agent, index) => (
          <button
            key={agent.id}
            ref={(el) => { itemRefs.current[index] = el; }}
            className={`w-full flex items-center gap-3 px-3 py-2.5 text-left transition-all duration-150 border-l-2 ${
              index === selectedIndex
                ? 'bg-slate-800 border-l-blue-500'
                : 'bg-transparent border-l-transparent hover:bg-slate-800/50'
            }`}
            onClick={() => onSelect(agent)}
            onMouseEnter={() => setSelectedIndex(index)}
            role="option"
            aria-selected={index === selectedIndex}
          >
            <span
              className={`flex-shrink-0 w-8 h-8 rounded-lg ${getAgentColor(
                agent.color
              )} flex items-center justify-center text-sm font-bold border`}
            >
              {agent.icon === 'database'
                ? '🗄'
                : agent.icon === 'activity'
                ? '📊'
                : '⚙'}
            </span>
            <div className="flex-1 min-w-0">
              <div className="flex items-center gap-2">
                <span className="text-sm font-medium text-slate-200 truncate">
                  @{agent.name.replace(/\s+/g, '-').toLowerCase()}
                </span>
                <span
                  className={`w-1.5 h-1.5 rounded-full flex-shrink-0 ${getStatusDot(
                    agent.status
                  )}`}
                />
              </div>
              <p className="text-xs text-slate-500 truncate mt-0.5">
                {agent.description}
              </p>
            </div>
          </button>
        ))}
      </div>
      <div className="px-3 py-1.5 bg-slate-800/30 border-t border-slate-700/30">
        <span className="text-[10px] text-slate-500">
          ↑↓ to navigate, Enter to select, Esc to close
        </span>
      </div>
    </div>
  );
});

export default AgentMention;
