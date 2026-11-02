// Protects registered acquisition drafts from all React Router transitions.
import { createContext, useCallback, useContext, useEffect, useRef, useState } from "react";
import { useBlocker } from "react-router";

const UnsavedNavigationContext = createContext(null);

export function UnsavedNavigationGuard({ children }) {
  const [hasUnsavedChanges, setHasUnsavedChanges] = useState(false);
  const dirtyRef = useRef(hasUnsavedChanges);
  const setDirty = useCallback(value => {
    const dirty = Boolean(value);
    dirtyRef.current = dirty;
    setHasUnsavedChanges(dirty);
  }, []);
  const shouldBlock = useCallback(() => dirtyRef.current, []);
  const blocker = useBlocker(shouldBlock);

  useEffect(() => {
    if (blocker.state !== "blocked") return;
    if (window.confirm("当前采集配置还有未保存的修改。离开后这些更改将丢失；是否继续离开？"))
      blocker.proceed();
    else
      blocker.reset();
  }, [blocker]);

  return <UnsavedNavigationContext.Provider value={setDirty}>{children}</UnsavedNavigationContext.Provider>;
}

export function useUnsavedNavigationRegistration() {
  return useContext(UnsavedNavigationContext);
}
