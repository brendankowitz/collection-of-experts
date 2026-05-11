import { useEffect, memo, useState } from 'react';
import { useChatStore } from './store/chatStore';
import { agentClient } from './services/agentClient';
import Sidebar from './components/Sidebar';
import ChatInterface from './components/ChatInterface';
import RepositoriesPanel from './components/RepositoriesPanel';

const App = memo(function App() {
  const darkMode = useChatStore((s) => s.darkMode);
  const setAgents = useChatStore((s) => s.setAgents);
  const [showRepositories, setShowRepositories] = useState(false);

  useEffect(() => {
    if (darkMode) {
      document.documentElement.classList.add('dark');
    } else {
      document.documentElement.classList.remove('dark');
    }
  }, [darkMode]);

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
      <Sidebar
        onManageRepositories={() => setShowRepositories(true)}
        onShowChat={() => setShowRepositories(false)}
        isRepositoriesOpen={showRepositories}
      />

      <main className="flex-1 min-w-0 flex flex-col h-full">
        {showRepositories ? (
          <RepositoriesPanel onClose={() => setShowRepositories(false)} />
        ) : (
          <ChatInterface />
        )}
      </main>
    </div>
  );
});

export default App;
