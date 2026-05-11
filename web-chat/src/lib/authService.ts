/**
 * authService.ts – MSAL-based authentication for Expert Agents web chat.
 *
 * Gated by the VITE_AUTH_MODE environment variable:
 *   - "disabled"  -> no-op (current dev behaviour)
 *   - "entra-id"  -> MSAL popup/redirect login; bearer token attached to API calls
 */

import type { IPublicClientApplication } from '@azure/msal-browser';

const AUTH_MODE = import.meta.env.VITE_AUTH_MODE ?? 'disabled';
const TENANT_ID = import.meta.env.VITE_ENTRA_TENANT_ID ?? '';
const CLIENT_ID = import.meta.env.VITE_ENTRA_CLIENT_ID ?? '';
const API_SCOPE = import.meta.env.VITE_ENTRA_API_SCOPE ?? 'api://expert-agents/Experts.Access';

// Lazy-load MSAL only when entra-id mode is active.
let msalInstance: IPublicClientApplication | null = null;

async function getMsal(): Promise<IPublicClientApplication> {
  if (msalInstance) return msalInstance;

  const { PublicClientApplication } = await import('@azure/msal-browser');

  const instance = new PublicClientApplication({
    auth: {
      clientId: CLIENT_ID,
      authority: `https://login.microsoftonline.com/${TENANT_ID}`,
      redirectUri: window.location.origin,
    },
    cache: {
      cacheLocation: 'sessionStorage',
    },
  });

  await instance.initialize();
  await instance.handleRedirectPromise();
  msalInstance = instance;
  return instance;
}

/**
 * Sign in the user and return an access token.
 * In "disabled" mode returns null.
 */
export async function signIn(): Promise<string | null> {
  if (AUTH_MODE !== 'entra-id') return null;

  const msal = await getMsal();
  const accounts = msal.getAllAccounts();

  if (accounts.length === 0) {
    await msal.loginPopup({ scopes: [API_SCOPE] });
  }

  return acquireToken();
}

/**
 * Acquire an access token silently (or via popup fallback).
 * Returns null in "disabled" mode.
 */
export async function acquireToken(): Promise<string | null> {
  if (AUTH_MODE !== 'entra-id') return null;

  const msal = await getMsal();
  const account = msal.getAllAccounts()[0];
  if (!account) return null;

  try {
    const result = await msal.acquireTokenSilent({
      account,
      scopes: [API_SCOPE],
    });
    return result.accessToken;
  } catch {
    const result = await msal.acquireTokenPopup({
      account,
      scopes: [API_SCOPE],
    });
    return result.accessToken;
  }
}

/**
 * Sign out the current user.
 * No-op in "disabled" mode.
 */
export async function signOut(): Promise<void> {
  if (AUTH_MODE !== 'entra-id') return;

  const msal = await getMsal();
  const account = msal.getAllAccounts()[0];
  if (account) {
    await msal.logoutPopup({ account });
  }
}

/**
 * Build Authorization headers.
 * Returns {} in "disabled" mode (no auth header added).
 */
export async function authHeaders(): Promise<Record<string, string>> {
  if (AUTH_MODE !== 'entra-id') return {};

  const token = await acquireToken();
  if (!token) return {};

  return { Authorization: `Bearer ${token}` };
}

export const isAuthEnabled = AUTH_MODE === 'entra-id';