# Resources

The game downloads about 7 GB of resources on first launch, and loads shop, outfit, banner, event and notice pictures while it runs. Both are served from this folder.

```
resources/
  1169/     669535640_1169_<version>_<build>_dyncures.ifs    Android archive
  1170/     669535640_1170_<version>_<build>_dyncures.ifs    iOS archive
  images/   cdn_hash_md5_size.bytes                          image index
            Default/<uri>/<name>.bytes                       images for every language
            En/<uri>/<name>.bytes                            images for one language (Chs, Jp, Ko, Fr, ...)
```

To keep everything somewhere else, set `ResourceArchive.Directory` in `appsettings.json` to an absolute path. A relative path is resolved against the folder you start the server from.

## Archives

Put each archive in a folder named after its platform's product id.

- Keep the official file name. The server reads the version from it.
- Put exactly one `.ifs` in each product folder.
- On first start the server computes the archive's MD5 and saves it next to it as `<file>.ifs.md5`. Later starts reuse it until the archive changes.

The archive is served at `/resources/<productId>/<file>` on the HTTP server. If players reach the server through a reverse proxy or a CDN (recommended for large scale), set `ResourceArchive.BaseUrl` to that public address.

## Images

Put the image mirror in `images/` with the image CDN's own layout.

- Keep every file exactly as the CDN sent it. The server does not decode or change them.
- Keep `cdn_hash_md5_size.bytes`. Without it the server logs a warning at startup.
- An image that is not here gets a 404, and the game shows its default picture instead. The game still works.

The images are served at `/cdn/...` on the HTTP server. `tools/build_apk.py` points the client there, on the same address as the login pages.
