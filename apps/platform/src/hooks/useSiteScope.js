// Keep page queries inside the selected site that the current identity can access.
import { useCallback, useMemo } from "react";
import { useSearchParams } from "react-router";
import { registeredSiteIds, useApi } from "./useApi";

export function useSiteScope({ enabled = true, edgeInterval = 0, allowAllSites = false } = {}) {
  const [searchParams, setSearchParams] = useSearchParams();
  const requestedSiteId = searchParams.get("siteId") || "";
  const identity = useApi(enabled ? "/api/v1/auth/me" : "", { enabled });
  const assignedSiteIds = Array.isArray(identity.data?.siteIds) ? identity.data.siteIds : [];
  const administrator = identity.data?.roles?.includes("platform.admin") || false;
  const edgeQuerySiteId = requestedSiteId || (assignedSiteIds.length === 1 ? assignedSiteIds[0] : "");
  const edgeQuery = edgeQuerySiteId ? `?siteId=${encodeURIComponent(edgeQuerySiteId)}` : "";
  const loadEdges = enabled && Boolean(edgeQuerySiteId || administrator);
  const edgeResponse = useApi(loadEdges ? `/api/edges${edgeQuery}` : "", {
    enabled: loadEdges,
    interval: edgeInterval,
  });
  const loadSiteDirectory = enabled && administrator && Boolean(edgeQuerySiteId);
  const siteDirectoryResponse = useApi(loadSiteDirectory ? "/api/edges" : "", {
    enabled: loadSiteDirectory,
  });
  const siteChoices = useMemo(() => {
    if (!enabled) return [];
    const requestedAdministratorSite = administrator && requestedSiteId ? [requestedSiteId] : [];
    const directoryData = loadSiteDirectory ? siteDirectoryResponse.data ?? edgeResponse.data : edgeResponse.data;
    return [...new Set([...assignedSiteIds, ...registeredSiteIds(directoryData), ...requestedAdministratorSite].filter(Boolean))]
      .sort((left, right) => left.localeCompare(right));
  }, [administrator, assignedSiteIds, edgeResponse.data, enabled, loadSiteDirectory, requestedSiteId, siteDirectoryResponse.data]);
  const invalidRequestedSite = Boolean(requestedSiteId && !siteChoices.includes(requestedSiteId));
  const siteId = requestedSiteId
    ? siteChoices.includes(requestedSiteId) ? requestedSiteId : ""
    : administrator && allowAllSites ? "" : siteChoices.length === 1 ? siteChoices[0] : "";
  const selectSite = useCallback(value => {
    setSearchParams(current => {
      const next = new URLSearchParams(current);
      if (value) next.set("siteId", value);
      else next.delete("siteId");
      return next;
    }, { replace: true });
  }, [setSearchParams]);
  const reload = useCallback(
    () => Promise.all([identity.reload(), edgeResponse.reload(), siteDirectoryResponse.reload()]),
    [edgeResponse.reload, identity.reload, siteDirectoryResponse.reload],
  );

  return {
    siteChoices,
    siteId,
    selectSite,
    invalidRequestedSite,
    loading: enabled && (identity.loading || (loadEdges && edgeResponse.loading) || (loadSiteDirectory && siteDirectoryResponse.loading)),
    error: enabled ? identity.error || (loadEdges ? edgeResponse.error : "") || (loadSiteDirectory ? siteDirectoryResponse.error : "") : "",
    edgeResponse,
    administrator,
    reload,
  };
}
