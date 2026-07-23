<template>
  <div v-if="total > pageSize" class="table-pagination">
    <el-pagination
      :current-page="page"
      :page-size="pageSize"
      :page-sizes="pageSizes"
      :total="total"
      :layout="compact ? 'prev, pager, next' : 'total, sizes, prev, pager, next'"
      @update:current-page="$emit('update:page', $event)"
      @update:page-size="$emit('update:pageSize', $event)"
    />
  </div>
</template>

<script setup>
defineProps({
  page: { type: Number, required: true },
  pageSize: { type: Number, required: true },
  total: { type: Number, required: true },
  pageSizes: { type: Array, default: () => [20, 50, 100] },
  compact: { type: Boolean, default: false },
});

defineEmits(["update:page", "update:pageSize"]);
</script>

<style scoped>
.table-pagination { display: flex; justify-content: flex-end; padding-top: 16px; }
</style>
