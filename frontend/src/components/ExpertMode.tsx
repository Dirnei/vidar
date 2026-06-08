import { createContext, useContext, useState, useEffect } from 'react';

const ExpertModeContext = createContext({ expert: false, toggle: () => {} });

export function ExpertModeProvider({ children }: { children: React.ReactNode }) {
  const [expert, setExpert] = useState(() => localStorage.getItem('vidar-expert') === 'true');

  useEffect(() => {
    localStorage.setItem('vidar-expert', String(expert));
  }, [expert]);

  return (
    <ExpertModeContext.Provider value={{ expert, toggle: () => setExpert(v => !v) }}>
      {children}
    </ExpertModeContext.Provider>
  );
}

export function useExpertMode() {
  return useContext(ExpertModeContext);
}
