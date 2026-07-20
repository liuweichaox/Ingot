import { defineComponent, h } from "vue";
import { ElButton, ElPopover } from "element-plus";

export default defineComponent({
  name: "JsonPopover",
  props: {
    label: { type: String, required: true },
    value: { type: Object, required: true },
  },
  setup(props) {
    return () => h(
      ElPopover,
      { width: 440, trigger: "click", placement: "left" },
      {
        reference: () => h(ElButton, { text: true, type: "primary" }, () => props.label),
        default: () => h("pre", { class: "json-preview" }, JSON.stringify(props.value, null, 2)),
      }
    );
  },
});
