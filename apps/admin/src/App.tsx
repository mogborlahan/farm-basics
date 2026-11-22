import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { AuthProvider } from "./auth/AuthProvider";
import { AdminShell } from "./components/AdminShell";

const queryClient = new QueryClient();

export function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <AdminShell />
      </AuthProvider>
    </QueryClientProvider>
  );
}

export default App;
