# Interoperable script flow

The Visual script list, Test Flow Builder, JSON, Python, and Robot views share one recursive node tree (`RecordedStep`). Editing or importing a supported construct updates that same tree, and the generators render the tree to each script format.

## JSON schema v2

Schema v2 stores top-level nodes in `nodes`. Ordinary action and verification entries remain leaf nodes. Control-flow nodes own child arrays:

```json
{
  "schemaVersion": 2,
  "datasets": {
    "users": ["alice", "bob"]
  },
  "nodes": [
    {
      "kind": "If",
      "condition": {
        "kind": "ElementExists",
        "alias": "LoginPage.txtUsername"
      },
      "hasElse": true,
      "thenSteps": [
        {
          "kind": "Action",
          "action": "SetValue",
          "alias": "LoginPage.txtUsername",
          "value": "${user}"
        }
      ],
      "elseSteps": []
    },
    {
      "kind": "Repeat",
      "repeatCount": 2,
      "bodySteps": []
    },
    {
      "kind": "ForEach",
      "loopVariable": "user",
      "loopSourceKind": "NamedDataset",
      "datasetName": "users",
      "bodySteps": []
    }
  ]
}
```

`ForEach` also accepts `loopSourceKind: "LiteralValues"` with `loopValues`, an array of strings. Use `${variable}` in action values to read the current loop value. Conditions are allow-listed: element exists, visible, enabled, text equals, and text contains.

Existing schema v1 documents with a flat `steps` array continue to load. They become a flat list of ordinary nodes and are written back as schema v2 the next time the JSON view is generated.

## Python and Robot round-trip subset

The IDE's Python and Robot generators emit structured `if`/`else`, `range`, and `FOR` blocks. Import recognizes those generated forms, their nested action calls, and IDE dataset declarations. The condition helper is shared by Python and Robot and uses the same driver API as action steps.

The parser intentionally rejects control-flow expressions it cannot map to the node tree, such as arbitrary Python conditions, comprehensions, and `while` loops. A bounded `While` node can be added after the first three structures round-trip reliably.

## Safety limits

- Fixed-count Repeat and each literal or named dataset are limited to 1,000 iterations/items.
- Control-flow nesting is limited to 64 levels and scripts to 10,000 nodes.
- `While` is not part of schema v2, so structured loops always have a finite bound.
