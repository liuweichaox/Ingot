
import "@testing-library/jest-dom/vitest";
import React from "react";

globalThis.React = React;

// React Flow expects ResizeObserver in the browser; jsdom tests assert behavior rather than layout measurements.
if (typeof globalThis.ResizeObserver === "undefined") {
  globalThis.ResizeObserver = class ResizeObserverStub {
    observe() {}
    unobserve() {}
    disconnect() {}
  };
}
