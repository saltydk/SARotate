# SARotate
For rotating Google Service Accounts to spread the API load in an attempt to avoid Rclone mount file access problems with heavy API traffic.

Parses the specified Service Account files and automatically identifies the projects that they are a part of and rotates between projects where possible to spread API usage across projects.

Heavily inspired by [SARotate](https://github.com/Visorask/SARotate) by [Visorask](https://github.com/Visorask) and with his permission we kept the name.

## Requirements:
Rclone v1.55 or newer.
Rclone mount with remote control enabled and authentication either disabled or using a known user/password.
Google Service Accounts placed in a directory.

## Installation:
Assumes you have fulfilled the above requirements. For information on Rclone remote control you can go [here](https://rclone.org/rc/). For help creating a lot of service accounts quickly you can use [safire](https://github.com/88lex/safire) or [sa-gen](https://github.com/88lex/sa-gen) which are both projects by [Lex](https://github.com/88lex).

We'll be using /opt/sarotate as the directory in this example. The below example assumes your user owns /opt already so change the commands accordingly if that isn't the case for your setup. Folder was chosen since the project has roots in a project using /opt as the main application storage area.

Create a directory for SARotate and enter it:
```shell
mkdir /opt/sarotate
cd /opt/sarotate
```
Download the latest binary:
```shell
curl -Ls https://api.github.com/repos/saltydk/sarotate/releases/latest | grep "browser_download_url" | cut -d '"' -f 4 | wget -qi -
chmod +x SARotate
```
Place a config.yaml in the same directory as the binary with the configuration described in the next section.


## Configuration:
Program expects a config.yaml in the working directory unless a custom path is specified.
```yaml
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

###### Rclone section:
```yaml
rclone:
  rclone_config: "/home/user/.config/rclone/rclone.conf" # The config loaded when querying rclone
  rc_user: "user" # Optional - Set if you have enabled Rclone authentication
  rc_pass: "pass" # Optional - Set if you have enabled Rclone authentication
  sleeptime: 300 # Delay between service account rotation
```

###### Remotes section:
```yaml
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

###### Notifications section:
```yaml
notification:
  errors_only: y # If you only want errors posted to apprise notications
  apprise: # List of apprise notifications. Add one or as many as you want
    - 'discord://<webhook>'
```
Look [here](https://github.com/caronc/apprise) for apprise instructions.

Before setting up the service below you should run SARotate manually and make sure it works.

## Service Example:
```ini
[Unit]
Description=sarotate     
After=network-online.target # Could also enter the rclone mount service name here instead and avoid SARotate complaining on system startup until rclone responds to commands.

[Service]
User=user # Change this to your username
Group=user # Change this to your username
Type=simple
WorkingDirectory=/opt/sarotate/
ExecStart=/opt/sarotate/SARotate
Restart=always
RestartSec=10

[Install]
WantedBy=default.target
```
You can install the above service example by placing the edited contents in a service file:
```shell
sudo nano /etc/systemd/system/sarotate.service
```
Then you can enable (starts on boot) and start the service:
```shell
sudo systemctl enable sarotate.service && sudo systemctl start sarotate.service
```

## Donations:
| Developers                                  | Roles              | Methods                                                                                                                                                                                                                                                                      |
|:------------------------------------------- |:------------------ |:------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
[salty](https://github.com/saltydk)         | Developer | [GitHub Sponsors](https://github.com/sponsors/saltydk); [Paypal](https://www.paypal.me/saltydk);
[Visorask](https://github.com/Visorask)         | Original Author | [GitHub Sponsors](https://github.com/sponsors/Visorask); [Paypal](https://paypal.me/RRussell603);
