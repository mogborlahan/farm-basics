import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import type { User, UserManager } from "oidc-client-ts";
import { createUserManager } from "./oidcClient";

interface AuthContextValue {
  user: User | null;
  isLoading: boolean;
  errorMessage: string | null;
  signIn: () => Promise<void>;
  signOut: () => Promise<void>;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({
  children,
  manager
}: {
  children: ReactNode;
  manager?: UserManager;
}) {
  const userManager = useMemo(() => manager ?? createUserManager(), [manager]);
  const [user, setUser] = useState<User | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  useEffect(() => {
    let active = true;

    async function bootstrapSession() {
      try {
        if (window.location.pathname === "/auth/callback") {
          const signedInUser = await userManager.signinRedirectCallback();
          if (active) {
            setUser(signedInUser);
            window.history.replaceState({}, document.title, "/");
          }
          return;
        }

        const existing = await userManager.getUser();
        if (active) {
          setUser(existing && !existing.expired ? existing : null);
        }
      } catch (error) {
        if (active) {
          setErrorMessage(error instanceof Error ? error.message : "Authentication handshake failed.");
        }
      } finally {
        if (active) {
          setIsLoading(false);
        }
      }
    }

    void bootstrapSession();

    const handleLoaded = (loadedUser: User) => {
      if (active) setUser(loadedUser);
    };
    const handleUnloaded = () => {
      if (active) setUser(null);
    };

    userManager.events.addUserLoaded(handleLoaded);
    userManager.events.addUserUnloaded(handleUnloaded);

    return () => {
      active = false;
      userManager.events.removeUserLoaded(handleLoaded);
      userManager.events.removeUserUnloaded(handleUnloaded);
    };
  }, [userManager]);

  const value: AuthContextValue = {
    user,
    isLoading,
    errorMessage,
    signIn: () => userManager.signinRedirect(),
    signOut: () => userManager.signoutRedirect()
  };

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) {
    throw new Error("useAuth must be used inside AuthProvider");
  }
  return ctx;
}
