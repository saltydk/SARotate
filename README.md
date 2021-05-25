# SARotate
For rotating Google Service Accounts to spread the API load in an attempt to avoid Rclone mount file access problems with heavy API traffic.

Parses the specified Service Account files and automatically identifies the projects that they are a part of and rotates between projects where possible to spread API usage across projects.

Heavily inspired by [SARotate](https://github.com/Visorask/SARotate) by [Visorask](https://github.com/Visorask) and with his permission we kept the name.

## Configuration:
Program expects a config.yml in the working directory unless a custom path is specified.
```
rclone:
  rclone_config: "/home/user/.config/rclone/rclone.conf"
  rc_user: "user"
  rc_pass: "pass"
  sleeptime: 300

remotes:
  '/opt/sa':
    seedbox-drive: localhost:5623
  '/opt/sa2':
    Movies: localhost:5629
    Movies-4K: localhost:5629
    Movies-Danish: localhost:5629
    TV: localhost:5629
    TV-4K: localhost:5629
    TV-Anime: localhost:5629

notification:
  errors_only: y
  apprise:
    - 'discord://<webhook>'
```

###### Rclone:
```
rclone:
  rclone_config: "/home/user/.config/rclone/rclone.conf" # The config loaded when querying rclone
  rc_user: "user" # Optional - Set if you have enabled Rclone authentication
  rc_pass: "pass" # Optional - Set if you have enabled Rclone authentication
  sleeptime: 300 # Delay between service account rotation
```

###### Remotes:
```
remotes:
  '/opt/sa': # Folder containing service accounts
    seedbox-drive: localhost:5623 # Remote that uses the above service accounts and its Rclone address
  '/opt/sa2': # Can add additional folder + remote pairings if needed
    Movies: localhost:5629
    Movies-4K: localhost:5629
    Movies-Danish: localhost:5629
    TV: localhost:5629
    TV-4K: localhost:5629
    TV-Anime: localhost:5629
```

###### Notifications:
```
notification:
  errors_only: y # If you only want errors posted to apprise notications
  apprise: # List of apprise notifications. Add one or as many as you want
    - 'discord://<webhook>'
```
Look [here](https://github.com/caronc/apprise) for apprise instructions.

## Service Example:
```
[Unit]
Description=sarotate     
After=network-online.target

[Service]
User=user
Group=user
Type=simple
WorkingDirectory=/opt/sarotate/
ExecStart=/opt/sarotate/SARotate
Restart=always
RestartSec=10

[Install]
WantedBy=default.target
```

## Donations:
| Developers                                  | Roles              | Methods                                                                                                                                                                                                                                                                      |
|:------------------------------------------- |:------------------ |:------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
[salty](https://github.com/saltydk)         | Developer | [GitHub Sponsors](https://github.com/sponsors/saltydk); [Paypal](https://www.paypal.me/saltydk);
[Visorask](https://github.com/Visorask)         | Original Author | [GitHub Sponsors](https://github.com/sponsors/Visorask); [Paypal](https://paypal.me/RRussell603);
