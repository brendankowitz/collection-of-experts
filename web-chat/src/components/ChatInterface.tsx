import {
  useState,
  useCallback,
  useRef,
  useEffect,
  memo,
} from 'react';
import {
  Send,
  Loader2,
  Sparkles,
  Zap,
  Menu,
} from 'lucide-react';
import { useChatStore, suggestedPrompts } from '../store/chatStore';
import { agentClient } from '../services/agentClient';
import { cn } from '../lib/utils';
import MessageList from './MessageList';
import AgentMention from './AgentMention';

const ChatInterface = memo(function ChatInterface() {
  const [input, setInput] = useState('');
  const [showMentions, setShowMentions] = useState(false);
  const [cursorPosition, setCursorPosition] = useState(0);
  const inputRef = useRef<HTMLTextAreaElement>(null);
  const cancelRef = useRef<(() => void) | null>(null);

  const messages = useChatStore((s) => s.messages);
  const selectedAgent = useChatStore((s) => s.selectedAgent);
  const isStreaming = useChatStore((s) => s.isStreaming);
  const sessionId = useChatStore((s) => s.sessionId);
  const toggleSidebar = useChatStore((s) => s.toggleSidebar);

  const selectedAgentData = useChatStore((s) =>
    s.agents.find((a) => a.id === selectedAgent)
  );

  // Cleanup on unmount
  useEffect(() => {
    return () => {
      if (cancelRef.current) {
        cancelRef.current();
      }
    };
  }, []);

  // Auto-resize textarea
  useEffect(() => {
    const el = inputRef.current;
    if (el) {
      el.style.height = 'auto';
      el.style.height = `${Math.min(el.scrollHeight, 200)}px`;
    }
  }, [input]);

  const handleInputChange = useCallback(
    (e: React.ChangeEvent<HTMLTextAreaElement>) => {
      const value = e.target.value;
      const cursor = e.target.selectionStart;
      setInput(value);
      setCursorPosition(cursor);

      // Check if @ was typed
      const textBeforeCursor = value.slice(0, cursor);
      const lastAtIndex = textBeforeCursor.lastIndexOf('@');

      if (lastAtIndex !== -1) {
        const textAfterAt = textBeforeCursor.slice(lastAtIndex + 1);
        // Show mentions if no space after @, or if we're typing a name
        if (!textAfterAt.includes(' ') || textAfterAt.trim().length > 0) {
          setShowMentions(true);
        } else {
          setShowMentions(false);
        }
      } else {
        setShowMentions(false);
      }
    },
    []
  );

  const handleAgentSelect = useCallback(
    (agent: { name: string }) => {
      const el = inputRef.current;
      if (!el) return;

      const cursor = el.selectionStart;
      const textBeforeCursor = input.slice(0, cursor);
      const lastAtIndex = textBeforeCursor.lastIndexOf('@');
      const beforeAt = input.slice(0, lastAtIndex);
      const afterCursor = input.slice(cursor);
      const agentMention = `@${agent.name.replace(/\s+/g, '-').toLowerCase()} `;
      const newValue = beforeAt + agentMention + afterCursor;

      setInput(newValue);
      setShowMentions(false);

      // Restore focus and cursor
      requestAnimationFrame(() => {
        const newPos = lastAtIndex + agentMention.length;
        el.focus();
        el.setSelectionRange(newPos, newPos);
      });
    },
    [input]
  );

  const handleSubmit = useCallback(async () => {
    const trimmedInput = input.trim();
    if (!trimmedInput || isStreaming || !selectedAgent) return;

    setInput('');
    if (inputRef.current) {
      inputRef.current.style.height = 'auto';
    }

    // Cancel previous stream if any
    if (cancelRef.current) {
      cancelRef.current();
      cancelRef.current = null;
    }

    try {
      const { cancel } = await agentClient.createMessage(
        selectedAgent,
        trimmedInput,
        sessionId
      );
      cancelRef.current = cancel;
    } catch (error) {
      console.error('Failed to send message:', error);
    }
  }, [input, isStreaming, selectedAgent, sessionId]);

  const handleKeyDown = useCallback(
    (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
      if (e.key === 'Enter' && !e.shiftKey) {
        // Don't submit if mention dropdown is open
        if (showMentions) {
          return;
        }
        e.preventDefault();
        handleSubmit();
      }
    },
    [handleSubmit, showMentions]
  );

  const handleSuggestedPrompt = useCallback(
    (prompt: string) => {
      if (isStreaming || !selectedAgent) return;

      // Cancel previous stream if any
      if (cancelRef.current) {
        cancelRef.current();
        cancelRef.current = null;
      }

      agentClient.createMessage(selectedAgent, prompt, sessionId).then(
        ({ cancel }) => {
          cancelRef.current = cancel;
        },
        (error) => {
          console.error('Failed to send message:', error);
        }
      );
    },
    [isStreaming, selectedAgent, sessionId]
  );

  const prompts = selectedAgent
    ? suggestedPrompts[selectedAgent] || []
    : [];

  const hasMessages = messages.length > 0;

  return (
    <div className="flex flex-col h-full bg-slate-950 relative">
      {/* Top Bar */}
      <header className="flex items-center justify-between px-4 py-3 border-b border-slate-800/60 bg-slate-950/80 backdrop-blur-md z-10">
        <div className="flex items-center gap-3">
          <button
            onClick={toggleSidebar}
            className="md:hidden p-2 rounded-lg hover:bg-slate-800 text-slate-400 transition-colors"
            aria-label="Open sidebar"
          >
            <Menu className="w-5 h-5" />
          </button>
          {selectedAgentData && (
            <div className="flex items-center gap-2.5">
              <div
                className={cn(
                  'w-8 h-8 rounded-lg flex items-center justify-center border',
                  selectedAgentData.color === 'fhir'
                    ? 'bg-emerald-500/10 border-emerald-500/20 text-emerald-400'
                    : selectedAgentData.color === 'healthcare'
                    ? 'bg-blue-500/10 border-blue-500/20 text-blue-400'
                    : 'bg-slate-700 border-slate-600 text-slate-400'
                )}
              >
                {selectedAgentData.icon === 'database'
                  ? '🗄'
                  : selectedAgentData.icon === 'activity'
                  ? '📊'
                  : '⚙'}
              </div>
              <div>
                <h2 className="text-sm font-semibold text-slate-200">
                  {selectedAgentData.name}
                </h2>
                <div className="flex items-center gap-1.5">
                  <span
                    className={cn(
                      'w-1.5 h-1.5 rounded-full',
                      selectedAgentData.status === 'online'
                        ? 'bg-emerald-500'
                        : selectedAgentData.status === 'busy'
                        ? 'bg-amber-500'
                        : 'bg-slate-500'
                    )}
                  />
                  <span className="text-[10px] text-slate-500 capitalize">
                    {selectedAgentData.status}
                  </span>
                </div>
              </div>
            </div>
          )}
        </div>
        <div className="flex items-center gap-2">
          {isStreaming && (
            <div className="flex items-center gap-1.5 px-2.5 py-1 rounded-lg bg-amber-500/10 border border-amber-500/20">
              <Loader2 className="w-3 h-3 text-amber-400 animate-spin" />
              <span className="text-[10px] font-medium text-amber-400">
                Streaming
              </span>
            </div>
          )}
        </div>
      </header>

      {/* Messages Area */}
      {hasMessages ? (
        <MessageList messages={messages} isStreaming={isStreaming} />
      ) : (
        /* Welcome Screen */
        <div className="flex-1 flex flex-col items-center justify-center px-4 overflow-y-auto">
          <div className="max-w-2xl w-full mx-auto text-center space-y-8 animate-fade-in">
            {/* Hero */}
            <div className="space-y-3">
              <div className="inline-flex items-center gap-2 px-3 py-1.5 rounded-full bg-slate-800/60 border border-slate-700/50">
                <Sparkles className="w-3.5 h-3.5 text-blue-400" />
                <span className="text-xs font-medium text-slate-400">
                  AI-Powered Expert Chat
                </span>
              </div>
              <h1 className="text-2xl sm:text-3xl font-bold text-slate-100 tracking-tight">
                Welcome to{' '}
                <span className="bg-gradient-to-r from-blue-400 to-indigo-400 bg-clip-text text-transparent">
                  Expert Agents
                </span>
              </h1>
              <p className="text-sm text-slate-500 max-w-md mx-auto leading-relaxed">
                Chat with specialized AI agents that have deep knowledge of
                Microsoft Healthcare technologies.
              </p>
            </div>

            {/* Agent Cards */}
            <div className="grid sm:grid-cols-2 gap-3 max-w-lg mx-auto">
              {useChatStore
                .getState()
                .agents.filter((a) => a.id !== 'system')
                .map((agent) => (
                  <button
                    key={agent.id}
                    onClick={() =>
                      useChatStore.getState().selectAgent(agent.id)
                    }
                    className={cn(
                      'flex items-center gap-3 p-4 rounded-xl text-left transition-all duration-200 border',
                      selectedAgent === agent.id
                        ? agent.color === 'fhir'
                          ? 'bg-emerald-500/5 border-emerald-500/30 shadow-sm shadow-emerald-500/5'
                          : 'bg-blue-500/5 border-blue-500/30 shadow-sm shadow-blue-500/5'
                        : 'bg-slate-800/30 border-slate-700/30 hover:bg-slate-800/50 hover:border-slate-600/40'
                    )}
                  >
                    <div
                      className={cn(
                        'w-10 h-10 rounded-xl flex items-center justify-center text-lg border',
                        agent.color === 'fhir'
                          ? 'bg-emerald-500/10 border-emerald-500/20'
                          : 'bg-blue-500/10 border-blue-500/20'
                      )}
                    >
                      {agent.icon === 'database' ? '🗄' : '📊'}
                    </div>
                    <div>
                      <p className="text-sm font-medium text-slate-200">
                        {agent.name}
                      </p>
                      <p className="text-xs text-slate-500 mt-0.5 line-clamp-2">
                        {agent.description.slice(0, 80)}...
                      </p>
                    </div>
                  </button>
                ))}
            </div>

            {/* Suggested Prompts */}
            {selectedAgent && prompts.length > 0 && (
              <div className="space-y-3 max-w-lg mx-auto">
                <div className="flex items-center gap-2 justify-center">
                  <Zap className="w-3.5 h-3.5 text-slate-500" />
                  <span className="text-xs font-medium text-slate-500 uppercase tracking-wider">
                    Suggested Prompts
                  </span>
                </div>
                <div className="grid gap-2">
                  {prompts.map((prompt, index) => (
                    <button
                      key={index}
                      onClick={() => handleSuggestedPrompt(prompt)}
                      disabled={isStreaming}
                      className={cn(
                        'text-left px-4 py-3 rounded-xl text-sm transition-all duration-200 border',
                        'bg-slate-800/30 border-slate-700/30 text-slate-400',
                        'hover:bg-slate-800/60 hover:border-slate-600/40 hover:text-slate-300',
                        'active:scale-[0.99]',
                        isStreaming && 'opacity-50 cursor-not-allowed'
                      )}
                    >
                      {prompt}
                    </button>
                  ))}
                </div>
              </div>
            )}

            {/* Tip */}
            <p className="text-xs text-slate-600">
              Type{' '}
              <kbd className="px-1.5 py-0.5 rounded bg-slate-800 border border-slate-700 text-slate-400 font-mono text-[10px]">
                @
              </kbd>{' '}
              to mention an agent • Press{' '}
              <kbd className="px-1.5 py-0.5 rounded bg-slate-800 border border-slate-700 text-slate-400 font-mono text-[10px]">
                Enter
              </kbd>{' '}
              to send
            </p>
          </div>
        </div>
      )}

      {/* Input Area */}
      <div className="shrink-0 border-t border-slate-800/60 bg-slate-950 px-4 py-3">
        <div className="max-w-3xl mx-auto relative">
          {/* Agent Mention Dropdown */}
          {showMentions && (
            <AgentMention
              text={input}
              cursorPosition={cursorPosition}
              onSelect={handleAgentSelect}
              onClose={() => setShowMentions(false)}
            />
          )}

          <div
            className={cn(
              'flex items-end gap-2 rounded-2xl border transition-all duration-200',
              'bg-slate-900 border-slate-700/50 focus-within:border-slate-600 focus-within:shadow-lg focus-within:shadow-blue-500/5',
              isStreaming && 'opacity-70'
            )}
          >
            <textarea
              ref={inputRef}
              value={input}
              onChange={handleInputChange}
              onKeyDown={handleKeyDown}
              placeholder={
                selectedAgent
                  ? `Ask the ${selectedAgentData?.name || 'agent'}...`
                  : 'Select an agent to start chatting...'
              }
              disabled={isStreaming || !selectedAgent}
              rows={1}
              className={cn(
                'flex-1 bg-transparent border-0 resize-none px-4 py-3.5',
                'text-sm text-slate-200 placeholder:text-slate-600',
                'focus:outline-none focus:ring-0',
                'max-h-[200px]',
                isStreaming && 'cursor-not-allowed'
              )}
              aria-label="Message input"
            />
            <div className="pb-2 pr-2">
              <button
                onClick={handleSubmit}
                disabled={!input.trim() || isStreaming}
                className={cn(
                  'flex items-center justify-center w-9 h-9 rounded-xl transition-all duration-200',
                  'focus:outline-none focus:ring-2 focus:ring-blue-500/50',
                  input.trim() && !isStreaming
                    ? 'bg-blue-600 hover:bg-blue-500 text-white shadow-lg shadow-blue-900/20 active:scale-95'
                    : 'bg-slate-800 text-slate-600 cursor-not-allowed'
                )}
                aria-label="Send message"
              >
                {isStreaming ? (
                  <Loader2 className="w-4 h-4 animate-spin" />
                ) : (
                  <Send className="w-4 h-4" />
                )}
              </button>
            </div>
          </div>

          {/* Footer hint */}
          <div className="flex items-center justify-between mt-2 px-1">
            <span className="text-[10px] text-slate-600">
              Expert Agents may produce inaccurate information. Verify important
              information.
            </span>
            {selectedAgentData && (
              <span
                className={cn(
                  'text-[10px] px-2 py-0.5 rounded-full border',
                  selectedAgentData.color === 'fhir'
                    ? 'text-emerald-400 border-emerald-500/20 bg-emerald-500/5'
                    : selectedAgentData.color === 'healthcare'
                    ? 'text-blue-400 border-blue-500/20 bg-blue-500/5'
                    : 'text-slate-400 border-slate-500/20'
                )}
              >
                @{selectedAgentData.name.replace(/\s+/g, '-').toLowerCase()}
              </span>
            )}
          </div>
        </div>
      </div>
    </div>
  );
});

export default ChatInterface;
