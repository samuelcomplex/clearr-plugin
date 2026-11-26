# Security policy

Clearr deletes files, so security problems are taken seriously.

## Reporting a problem

Please **don't open a public issue** for a security problem. Use GitHub's private reporting instead:
go to the repository's **Security** tab and click **Report a vulnerability**.
You'll get a reply within a week, and a fix as fast as the problem calls for.

Useful to include: the Clearr version (Dashboard → Plugins), your Jellyfin version, and steps to reproduce.

## Supported versions

Only the latest release gets security fixes. Updates arrive through Jellyfin's plugin catalog.

## How the plugin protects itself

- Its API is only open to Jellyfin administrators; Jellyfin handles sign-in.
- **Deleting:** nothing is deleted unless the mode is **Delete**, checked again before every single item.
  Deleting through Jellyfin (for items Sonarr/Radarr don't manage) is off unless you turn it on.
- The decision logic and its safety tests live in [Clearr](https://github.com/samuelcomplex/clearr), shared by the app and the plugin.
