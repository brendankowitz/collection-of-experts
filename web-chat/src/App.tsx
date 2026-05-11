import { useEffect, memo } from 'react';
import { useChatStore } from './store/chatStore';
import { agentClient } from './services/agentClient';
import Sidebar from './components/Sidebar';
import ChatInterface from './components/ChatInterface';

const App = memo(function App() {
  const darkMode = useChatStore((s) => s.darkMode);
  const setAgents = useChatStore((s) => s.setAgents);

  // Apply dark mode class
  useEffect(() => {
    if (darkMode) {
      document.documentElement.classList.add('dark');
    } else {
      document.documentElement.classList.remove('dark');
    }
  }, [darkMode]);

  // Fetch agents on mount
  useEffect(() => {
    const fetchAgents = async () => {
      try {
        const agents = await agentClient.getAgents();
        setAgents(agents);
      } catch (error) {
        console.error('Failed to fetch agents:', error);
      }
    };

    fetchAgents();
  }, [setAgents]);

  return (
    <div className="flex h-screen w-screen bg-slate-950 text-slate-100 overflow-hidden">
      {/* Sidebar */}
      <Sidebar />

      {/* Main Chat Area */}
      <main className="flex-1 min-w-0 flex flex-col h-full">
        <ChatInterface />
      </main>
    </div>
  );
});

export default App;
