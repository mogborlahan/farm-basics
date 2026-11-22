import { UserManager, WebStorageStateStore } from "oidc-client-ts";

const appUrl = import.meta.env.VITE_APP_URL ?? window.location.origin;

export const oidcConfig = {
  authority: import.meta.env.VITE_KEYCLOAK_AUTHORITY ?? "http://localhost:8180/realms/farm-basics",
  client_id: import.meta.env.VITE_KEYCLOAK_CLIENT_ID ?? "farm-basics-admin",
  redirect_uri: `${appUrl}/auth/callback`,
  post_logout_redirect_uri: appUrl,
  response_type: "code",
  scope: "openid profile email",
  userStore: new WebStorageStateStore({ store: window.sessionStorage })
};

export function createUserManager(): UserManager {
  return new UserManager(oidcConfig);
}
