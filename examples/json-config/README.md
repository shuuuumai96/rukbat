# JSON configuration update

Enable `service.enabled`, remove the optional top-level `obsolete` property, and write compact UTF-8
JSON. `service` must exist and be an object. All other data is retained, including unknown nested
properties and number tokens that do not fit a Rukbat number type.

```powershell
rukbat run examples/json-config/main.rukbat -- config.json updated.json
rukbat build examples/json-config/main.rukbat
powershell -NoProfile -File ./examples/json-config/main.ps1 config.json updated.json
```

Example input:

```json
{"service":{"enabled":false,"extra":{"large":9007199254740993}},"obsolete":1,"other":null}
```

Output:

```json
{"other":null,"service":{"enabled":true,"extra":{"large":9007199254740993}}}
```

Invalid JSON, a missing `service`, or a non-object `service` exits with code 2 before saving output.
Existing output is untouched on input/validation failure. Output has no BOM or trailing newline;
object key order, whitespace, and escape spelling are normalized. The entire document is held in
memory. Choose separate input and output paths to retain the input.
