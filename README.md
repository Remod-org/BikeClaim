# BikeClaim
## Basic ownership management for Rust bikes (pedal and motorbike)

Tired of some clown stealing the bike you just bought or found?  This plugin allows players to maintain ownership and optionally prevent others from riding off with them.

Bikes may be claimed by mounting or via the chat command /bclaim.

Claimed bikes can be released via the chat command /brelease or through the use of a timer configured by the admin.

Users with permission may also spawn and remove their owned bikes.

Purchased bikes should also become managed by the plugin.

Limits may be set for standard and VIP users.

### Configuration
```json
{
  "Options": {
    "useClans": false,
    "useFriends": false,
    "useTeams": false,
    "debug": false,
    "SetOwnerOnFirstMount": true,
    "ReleaseOwnerOnbike": false,
    "RestrictMounting": true,
    "RestrictStorage": true,
    "EnableTimer": false,
    "EnableLimit": true,
    "AllowDecay": false,
    "AllowDamage": true,
    "TCPreventDamage": true,
    "TCMustBeAuthorized": true,
    "ReleaseTime": 600.0,
    "Limit": 2.0,
    "VIPLimit": 5.0
  },
  "Version": {
    "Major": 1,
    "Minor": 0,
    "Patch": 1
  }
}
```

- `useClans/useFriends/useTeams` -- Use Friends, Clans, or Rust teams to determine accessibility of an owned bike.  This allows friends to share bikes.
- `SetOwnerOnFirstMount` -- Sets ownership of an unowned bike on mount.
- `EnableTimer` -- Enable timed release of bike ownership after the time specified by ReleaseTime.
- `ReleaseTime` -- Sets the time IN SECONDS to maintain bike ownership if EnableTimer is true.  Must be a numerical value.
- `ReleaseOwnerOnbike` -- Release ownership of a bike while the owner is mounted after ReleaseTime has been reached.
- `RestrictMounting` -- Restrict mounting of owned bikes to the owner.  If false, you can use other plugins to manage this such as PreventLooting.
- `EnableLimit` -- Enable limit of total claimed bike count per player.
- `AllowDecay` -- If true, standard decay will apply to spawned or claimed bikes.
- `AllowDamage` -- If true, allow bike damage from other players, etc.  Note that this may conflict with NextGenPVE, et al, if configured to protect bikes.
- `TCPreventDamage` -- If AllowDamage is true, block damage if in building privilege.  See below...
- `TCMustBeAuthorized` -- If TCPreventDamage is true, set this true to require that the bike owner be registered on the local TC.  In other words, just being in ANY TC range would NOT prevent damage, so the attacked player can kill the attacker's bike from the comfort of their base, etc.
- `Limit` -- Limit for users with claim permission.
- `VIPLimit` -- Limit for users with vip permission.

### Permissions

- `bikeclaim.claim` -- Allows player claim and release bikes.
- `bikeclaim.spawn` -- Allows player to spawn or remove a bike.
- `bikeclaim.motorspawn` -- Allows player to spawn or remove a motorbike.
- `bikeclaim.sidecarspawn` -- Allows player to spawn or remove a motorbike with sidecar.
- `bikeclaim.trikespawn` -- Allows player to spawn or remove a trike.
- `bikeclaim.find` -- Allows player to show the location of their nearest claimed bike.
- `bikeclaim.vip` -- Gives player vip limits when limit is in use.

## Commands

- `/bclaim` - Claim the bike you're looking at (requires bikes.claim permission).  If the bike is owned by the server, this should work.
- `/brelease` - Release ownership of the bike you're looking at (requires bikes.claim permission).
- `/bremove` - Kill the bike in front of you (requires bikeclaim.spawn permission and ownership of the bike).  You may then enjoy some delicious bike meat.
- `/bfind` - Show location of nearest owned bike
- `/binfo` - Show basic info about a bike (Requires bikeclaim.claim permission, but can be used on any bike.)
- `/bspawn` - Spawn a new bike in front of you (requires bikeclaim.spawn permission).
- `/mbspawn` - Spawn a new motorbike in front of you (requires bikeclaim.motorspawn permission).
- `/msspawn` - Spawn a new motorbike with sidecar in front of you (requires bikeclaim.sidecarspawn permission).
- `/mtspawn` - Spawn a new trike in front of you (requires bikeclaim.trikespawn permission).

