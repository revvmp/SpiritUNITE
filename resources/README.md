# Resource archives

The game downloads about 7 GB of resources on first launch. It is served from this folder.

Put each archive in a folder named after its platform's product id:

```
resources/
  1169/   669535640_1169_<version>_<build>_dyncures.ifs    Android
  1117/   669535640_1117_<version>_<build>_dyncures.ifs    iOS
```

- Keep the official file name. The server reads the version from it.
- Put exactly one `.ifs` in each product folder.
- On first start the server computes the archive's MD5 and saves it next to it as `<file>.ifs.md5`. Later starts reuse it until the archive changes.

To keep the archives somewhere else, set `ResourceArchive.Directory` in `appsettings.json` to an absolute path. A relative path is resolved against the folder you start the server from.

The archive is served at `/resources/<productId>/<file>` on the HTTP server. If players reach the server through a reverse proxy or a CDN, set `ResourceArchive.BaseUrl` to that public address.
