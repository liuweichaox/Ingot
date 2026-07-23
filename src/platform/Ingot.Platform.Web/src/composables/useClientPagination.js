import { computed, ref, watch } from "vue";

export function useClientPagination(source, initialPageSize = 20) {
  const page = ref(1);
  const pageSize = ref(initialPageSize);
  const total = computed(() => source.value?.length || 0);
  const pageCount = computed(() => Math.max(1, Math.ceil(total.value / pageSize.value)));
  const pagedItems = computed(() => {
    const start = (page.value - 1) * pageSize.value;
    return (source.value || []).slice(start, start + pageSize.value);
  });

  watch([total, pageSize], () => {
    page.value = Math.min(page.value, pageCount.value);
  });

  function resetPage() {
    page.value = 1;
  }

  return { page, pageSize, total, pagedItems, resetPage };
}
