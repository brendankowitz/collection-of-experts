import { memo, useRef, useEffect, useCallback, useState } from 'react';
import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import { Prism as SyntaxHighlighter } from 'react-syntax-highlighter';
import { oneDark } from 'react-syntax-highlighter/dist/esm/styles/prism';
import { Copy, Check, Bot, User, AlertCircle } from 'lucide-react';
import { useChatStore } from '../store/chatStore';
import { cn } from '../lib/utils';

interface MessageListProps {
  messages: ReturnType<typeof useChatStore.getState>['messages'];
  isStreaming: boolean;
}

// Code block with copy button
const CodeBlock = memo(function CodeBlock({
  language,
  value,
}: {
  language: string;
  value: string;
}) {
  const [copied, setCopied] = useState(false);

  const handleCopy = useCallback(() => {
    navigator.clipboard.writeText(value).then(() => {
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    });
  }, [value]);

  return (
    <div className="code-block-wrapper">
      <div className="code-block-header">
        <span className="code-block-title">
          {language || 'code'}
        </span>
        <button
          onClick={handleCopy}
          className={cn(
            'flex items-center gap-1.5 px-2.5 py-1 rounded-lg text-xs font-medium',
            'transition-all duration-200',
            copied
              ? 'bg-emerald-500/15 text-emerald-400'
              : 'bg-slate-800 text-slate-400 hover:bg-slate-700 hover:text-slate-200'
          )}
          aria-label={copied ? 'Copied!' : 'Copy code'}
        >
          {copied ? (
            <>
              <Check className="w-3 h-3" />
              Copied
            </>
          ) : (
            <>
              <Copy className="w-3 h-3" />
              Copy
            </>
          )}
        </button>
      </div>
      <SyntaxHighlighter
        language={language || 'text'}
        style={oneDark}
        customStyle={{
          margin: 0,
          padding: '1rem 1.25rem',
          background: '#0c1222',
          fontSize: '0.8125rem',
          lineHeight: '1.7',
          borderRadius: '0 0 0.75rem 0.75rem',
        }}
        codeTagProps={{
          style: {
            fontFamily:
              'JetBrains Mono, Fira Code, Cascadia Code, SF Mono, Menlo, Consolas, monospace',
          },
        }}
      >
        {value}
      </SyntaxHighlighter>
    </div>
  );
});

// Typing indicator
const TypingIndicator = memo(function TypingIndicator() {
  return (
    <div className="typing-indicator" aria-label="Agent is typing">
      <span
        className="typing-dot animate-pulse-dot"
        style={{ animationDelay: '0ms' }}
      />
      <span
        className="typing-dot animate-pulse-dot animation-delay-150"
        style={{ animationDelay: '150ms' }}
      />
      <span
        className="typing-dot animate-pulse-dot animation-delay-300"
        style={{ animationDelay: '300ms' }}
      />
    </div>
  );
});

// Individual message component
const MessageItem = memo(function MessageItem({
  message,
  isLast,
  isStreaming,
}: {
  message: ReturnType<typeof useChatStore.getState>['messages'][0];
  isLast: boolean;
  isStreaming: boolean;
}) {
  const agents = useChatStore((s) => s.agents);
  const agent = message.agentId
    ? agents.find((a) => a.id === message.agentId)
    : null;

  const getAgentBadge = (): string | undefined => {
    if (!agent) return undefined;
    switch (agent.color) {
      case 'fhir':
        return 'agent-pill-fhir';
      case 'healthcare':
        return 'agent-pill-healthcare';
      default:
        return 'agent-pill-system';
    }
  };

  const getAgentIcon = () => {
    if (!agent) return <Bot className="w-4 h-4" />;
    switch (agent.icon) {
      case 'database':
        return (
          <span className="text-sm" role="img" aria-label="database">
            🗄
          </span>
        );
      case 'activity':
        return (
          <span className="text-sm" role="img" aria-label="activity">
            📊
          </span>
        );
      default:
        return <Bot className="w-4 h-4" />;
    }
  };

  const showTyping = isLast && isStreaming && message.role === 'assistant';
  const hasContent = message.content.trim().length > 0;

  return (
    <div
      className={cn('animate-fade-in-up', {
        'flex flex-row-reverse': message.role === 'user',
        'flex flex-row': message.role === 'assistant',
        'flex justify-center': message.role === 'system',
      })}
    >
      {/* Avatar for user */}
      {message.role === 'user' && (
        <div className="flex-shrink-0 ml-3 w-8 h-8 rounded-full bg-blue-600 flex items-center justify-center shadow-lg shadow-blue-900/30">
          <User className="w-4 h-4 text-white" />
        </div>
      )}

      {/* Avatar for assistant */}
      {message.role === 'assistant' && (
        <div
          className={cn(
            'flex-shrink-0 mr-3 w-8 h-8 rounded-full flex items-center justify-center',
            agent?.color === 'fhir'
              ? 'bg-emerald-500/15 border border-emerald-500/20 text-emerald-400'
              : agent?.color === 'healthcare'
              ? 'bg-blue-500/15 border border-blue-500/20 text-blue-400'
              : 'bg-slate-700 border border-slate-600 text-slate-400'
          )}
        >
          {getAgentIcon()}
        </div>
      )}

      {/* Message Bubble */}
      <div
        className={cn(
          'max-w-[85%] sm:max-w-[75%]',
          message.role === 'user'
            ? 'message-user'
            : message.role === 'assistant'
            ? 'message-assistant'
            : 'message-system'
        )}
      >
        {/* Agent badge for assistant */}
        {message.role === 'assistant' && agent && (
          <div className="flex items-center gap-2 mb-2">
            <span className={getAgentBadge()}>
              {getAgentIcon()}
              {agent.name}
            </span>
            {message.status === 'streaming' && (
              <span className="text-[10px] text-amber-400/80 animate-pulse">
                streaming...
              </span>
            )}
            {message.status === 'error' && (
              <span className="flex items-center gap-1 text-[10px] text-red-400">
                <AlertCircle className="w-3 h-3" />
                Error
              </span>
            )}
          </div>
        )}

        {/* Message Content */}
        {hasContent && (
          <div className="prose prose-invert prose-sm max-w-none">
            <ReactMarkdown
              remarkPlugins={[remarkGfm]}
              components={{
                code({ className, children, ...props }) {
                  const match = /language-(\w+)/.exec(className || '');
                  const code = String(children).replace(/\n$/, '');

                  if (match) {
                    return (
                      <CodeBlock
                        language={match[1]}
                        value={code}
                      />
                    );
                  }

                  return (
                    <code
                      className={cn(
                        'px-1.5 py-0.5 rounded-md bg-slate-700/50 text-slate-300 font-mono text-sm border border-slate-600/30'
                      )}
                      {...props}
                    >
                      {children}
                    </code>
                  );
                },
                pre({ children }) {
                  return <div className="my-0">{children}</div>;
                },
                table({ children }) {
                  return (
                    <div className="overflow-x-auto my-3 rounded-xl border border-slate-700/50">
                      <table className="w-full text-sm border-collapse">
                        {children}
                      </table>
                    </div>
                  );
                },
                thead({ children }) {
                  return (
                    <thead className="bg-slate-800">{children}</thead>
                  );
                },
                th({ children }) {
                  return (
                    <th className="text-left px-3 py-2 font-medium text-slate-300 border-b border-slate-700">
                      {children}
                    </th>
                  );
                },
                td({ children }) {
                  return (
                    <td className="px-3 py-2 border-b border-slate-800/50 text-slate-400">
                      {children}
                    </td>
                  );
                },
              }}
            >
              {message.content}
            </ReactMarkdown>
          </div>
        )}

        {/* Typing indicator */}
        {showTyping && !hasContent && (
          <div className="py-3">
            <TypingIndicator />
          </div>
        )}

        {/* Timestamp */}
        <div
          className={cn(
            'text-[10px] mt-2',
            message.role === 'user'
              ? 'text-blue-300/60 text-right'
              : 'text-slate-600'
          )}
        >
          {message.timestamp.toLocaleTimeString('en-US', {
            hour: 'numeric',
            minute: '2-digit',
            hour12: true,
          })}
        </div>
      </div>
    </div>
  );
});

const MessageList = memo(function MessageList({
  messages,
  isStreaming,
}: MessageListProps) {
  const scrollRef = useRef<HTMLDivElement>(null);
  const bottomRef = useRef<HTMLDivElement>(null);
  const shouldAutoScroll = useRef(true);

  // Track if user is near bottom
  const handleScroll = useCallback(() => {
    if (!scrollRef.current) return;
    const { scrollTop, scrollHeight, clientHeight } = scrollRef.current;
    const distanceToBottom = scrollHeight - scrollTop - clientHeight;
    shouldAutoScroll.current = distanceToBottom < 100;
  }, []);

  // Auto-scroll to bottom
  useEffect(() => {
    if (shouldAutoScroll.current && bottomRef.current) {
      bottomRef.current.scrollIntoView({ behavior: 'smooth' });
    }
  }, [messages, isStreaming]);

  if (messages.length === 0) {
    return null;
  }

  return (
    <div
      ref={scrollRef}
      onScroll={handleScroll}
      className="flex-1 overflow-y-auto px-4 py-6 space-y-5"
      role="log"
      aria-label="Chat messages"
      aria-live="polite"
    >
      {messages.map((msg, index) => (
        <MessageItem
          key={msg.id}
          message={msg}
          isLast={index === messages.length - 1}
          isStreaming={isStreaming}
        />
      ))}
      <div ref={bottomRef} />
    </div>
  );
});

export default MessageList;
