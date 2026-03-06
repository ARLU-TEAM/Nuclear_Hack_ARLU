# T-FLEX DOCs: where to get DLLs

For external .NET apps, DLLs are taken from the installed T-FLEX DOCs client:

- default path: `C:\Program Files (x86)\T-FLEX DOCs 17\Program`
- your app already supports this path via `TFlex:ClientProgramDirectory`
- runtime resolver expects `TFlex.PdmFramework.Resolve.dll` in that directory

Minimum project references in this repo:

- `TFlex.DOCs.Common.dll`
- `TFlex.DOCs.Model.dll`

At runtime, additional dependencies are loaded from `ClientProgramDirectory` by `AssemblyResolver`.

If connection fails with `FileNotFoundException`, install T-FLEX DOCs client (same major/minor version as server) and ensure `ClientProgramDirectory` points to the `Program` folder.

## Local secure config

Use `appsettings.Local.json` (ignored by git) for real credentials:

```json
{
  "TFlex": {
    "Server": "31.29.180.7:21325",
    "UserName": "Администратор",
    "Password": "*****",
    "UseAccessToken": false
  }
}
```
