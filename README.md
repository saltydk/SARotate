# SARotate
[![Discord](https://img.shields.io/discord/853755447970758686)](https://discord.gg/ugfKXpFND8)

For rotating Google Service Accounts to spread the API load in an attempt to avoid Rclone mount file access problems with heavy API traffic.

Parses the specified Service Account files and automatically identifies the projects that they are a part of and rotates between projects where possible to spread API usage across projects.

Heavily inspired by [SARotate](https://github.com/Visorask/SARotate) by [Visorask](https://github.com/Visorask) and with his permission we kept the name.

## Requirements:
[Rclone](https://github.com/rclone/rclone) v1.55 or newer.
Rclone mount with remote control enabled and authentication either disabled or using a known user/password.
Google Service Accounts placed in a directory.

[Apprise](https://github.com/caronc/apprise) - For notications if enabled.

## Installation:
Assumes you have fulfilled the above requirements. For information on Rclone remote control you can go [here](https://rclone.org/rc/). For help creating a lot of service accounts quickly you can use [safire](https://github.com/88lex/safire) or [sa-gen](https://github.com/88lex/sa-gen) which are both projects by [Lex](https://github.com/88lex).

We'll be using /opt/sarotate as the directory in this example. The below example assumes your user owns /opt already so change the commands accordingly if that isn't the case for your setup. Folder location was chosen due to this project having connections to [Saltbox](https://github.com/saltyorg/Saltbox) which uses /opt for apps in the ecosystem around it.

Create a directory for SARotate and enter it:
```shell
mkdir /opt/sarotate && cd /opt/sarotate
```
Download the latest Linux x64 binary: (Options for the last grep include linux-arm, linux-arm64, linux-musl-x64 and linux-x64)
```shell
curl -L "$(curl -Ls https://api.github.com/repos/saltydk/sarotate/releases/latest | grep "browser_download_url" | cut -d '"' -f 4 | grep "linux-x64")" -o SARotate
```

```shell
chmod +x SARotate
```

Place a config.yaml in the same directory as the binary with the configuration described in the next section.


## Configuration:
Program expects a config.yaml in the working directory unless a custom path is specified.
```yaml
rclone:
  sleeptime: 300

remotes:
  '/opt/sa':
    seedbox-drive:
      address: localhost:5623
      user: username
      pass: password
  '/opt/sa2':
    Movies:
      address: localhost:5629
      user: username
      pass: password
    Movies-4K:
      address: localhost:5629
      user: username
      pass: password
    Movies-Danish:
      address: localhost:5629
      user: username
      pass: password
    TV:
      address: localhost:5629
      user: username
      pass: password
    TV-4K:
      address: localhost:5629
      user: username
      pass: password
    TV-Anime:
      address: localhost:5629
      user: username
      pass: password

notification:
  errors_only: y
  apprise:
    - 'discord://<webhook>'
```

###### Rclone section:
```yaml
rclone:
  sleeptime: 300 # Delay between service account rotation
```

###### Remotes section:
```yaml
remotes:
  '/opt/sa': # Folder containing service accounts
    seedbox-drive: # Name of remote specified in rclone.conf (case sensitive)
      address: localhost:5623 # IP/hostname and port used for rclone remote control
      user: username # Optional - Set if you have enabled Rclone authentication
      pass: password # Optional - Set if you have enabled Rclone authentication
  '/opt/sa2': # Can add additional folder + remote pairings as needed
    Movies: # Name of remote specified in rclone.conf (case sensitive)
      address: localhost:5629 # IP/hostname and port used for rclone remote control
      user: username # Optional - Set if you have enabled Rclone authentication
      pass: password # Optional - Set if you have enabled Rclone authentication
    Movies-4K: # Name of remote specified in rclone.conf (case sensitive)
      address: localhost:5629 # IP/hostname and port used for rclone remote control
      user: username # Optional - Set if you have enabled Rclone authentication
      pass: password # Optional - Set if you have enabled Rclone authentication
    Movies-Danish: # Name of remote specified in rclone.conf (case sensitive)
      address: localhost:5629 # IP/hostname and port used for rclone remote control
      user: username # Optional - Set if you have enabled Rclone authentication
      pass: password # Optional - Set if you have enabled Rclone authentication
    TV: # Name of remote specified in rclone.conf (case sensitive)
      address: localhost:5629 # IP/hostname and port used for rclone remote control
      user: username # Optional - Set if you have enabled Rclone authentication
      pass: password # Optional - Set if you have enabled Rclone authentication
    TV-4K: # Name of remote specified in rclone.conf (case sensitive)
      address: localhost:5629 # IP/hostname and port used for rclone remote control
      user: username # Optional - Set if you have enabled Rclone authentication
      pass: password # Optional - Set if you have enabled Rclone authentication
    TV-Anime: # Name of remote specified in rclone.conf (case sensitive)
      address: localhost:5629 # IP/hostname and port used for rclone remote control
      user: username # Optional - Set if you have enabled Rclone authentication
      pass: password # Optional - Set if you have enabled Rclone authentication
```

###### Notifications section:
```yaml
notification:
  errors_only: y # If you only want errors posted to apprise notications
  apprise: # List of apprise notifications. Add one or as many as you want
    - 'discord://<webhook>'
```
Look [here](https://github.com/caronc/apprise) for apprise instructions.

Set to empty string to disable
```yaml
notification:
  errors_only: y
  apprise:
    - ''
```

Before setting up the service below you should run SARotate manually and make sure it works.

## Service Example:
```ini
[Unit]
Description=sarotate     
After=network-online.target

[Service]
User=user
Group=user
Type=simple
WorkingDirectory=/opt/sarotate/
ExecStart=/opt/sarotate/SARotate

[Install]
WantedBy=default.target
```
Edit the user and group to your existing user and if you want to avoid initial notification errors on boot it is probably a good idea to edit the After=network-online.target to the services used by your mount(s).

You can install the above service example by placing the edited contents in a service file:
```shell
sudo nano /etc/systemd/system/sarotate.service
```
Then you can enable (starts on boot) and start the service:
```shell
sudo systemctl enable sarotate.service && sudo systemctl start sarotate.service
```

## Donations:
| Developers                              | Roles           | Methods                                                                                           |
|:----------------------------------------|:----------------|:--------------------------------------------------------------------------------------------------|
| [salty](https://github.com/saltydk)     | Developer       | [GitHub Sponsors](https://github.com/sponsors/saltydk); [Paypal](https://www.paypal.me/saltydk);  |
| [Visorask](https://github.com/Visorask) | Original Author | [GitHub Sponsors](https://github.com/sponsors/Visorask); [Paypal](https://paypal.me/RRussell603); |  
