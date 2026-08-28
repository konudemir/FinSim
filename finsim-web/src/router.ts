import { useSyncExternalStore } from "react";

const LOCATION_CHANGE = "locationchange";

function subscribe(callback: () => void): () => void {
  window.addEventListener("popstate", callback);
  window.addEventListener(LOCATION_CHANGE, callback);
  return () => {
    window.removeEventListener("popstate", callback);
    window.removeEventListener(LOCATION_CHANGE, callback);
  };
}

function getSnapshot(): string {
  return window.location.pathname;
}

export function usePath(): string {
  return useSyncExternalStore(subscribe, getSnapshot);
}

export function navigate(path: string): void {
  window.history.pushState(null, "", path);
  window.dispatchEvent(new Event(LOCATION_CHANGE));
}

export function replacePath(path: string): void {
  window.history.replaceState(null, "", path);
  window.dispatchEvent(new Event(LOCATION_CHANGE));
}
