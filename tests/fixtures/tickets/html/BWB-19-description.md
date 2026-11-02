### Steps To Reproduce

1. Check the Web Installed version on the admin dashboard
2. Run 'docker ps' on the server
3. Compare the bitwarden/web version to the Web Installed version

### Expected Result

The bitwarden/web version from 'docker ps' should match the Web Installed version on the admin dashboard.

### Actual Result

Web Installed shows 2022.6.1 while 'docker ps' shows bitwarden/web:2022.6.2

### Screenshots or Videos

![image](https://user-images.githubusercontent.com/50854322/180883871-a2b33113-d823-4bcd-baaf-6dab402b12d3.png)

### Additional Context

docker ps
CONTAINER ID   IMAGE                              COMMAND            CREATED         STATUS                   PORTS                                                                                    NAMES
5cfe1900db5c   bitwarden/nginx:2022.6.2           "/entrypoint.sh"   2 minutes ago   Up 2 minutes (healthy)   80/tcp, 0.0.0.0:80->8080/tcp, :::80->8080/tcp, 0.0.0.0:443->8443/tcp, :::443->8443/tcp   bitwarden-nginx
768b8627dd14   bitwarden/admin:2022.6.2           "/entrypoint.sh"   2 minutes ago   Up 2 minutes (healthy)   5000/tcp                                                                                 bitwarden-admin
606ad4d6fd0d   bitwarden/mssql:2022.6.2           "/entrypoint.sh"   2 minutes ago   Up 2 minutes (healthy)                                                                                            bitwarden-mssql
8834ab70469e   bitwarden/identity:2022.6.2        "/entrypoint.sh"   2 minutes ago   Up 2 minutes (healthy)   5000/tcp                                                                                 bitwarden-identity
b3fdc2b00614   bitwarden/sso:2022.6.2             "/entrypoint.sh"   2 minutes ago   Up 2 minutes (healthy)   5000/tcp                                                                                 bitwarden-sso
38a4030fb3b5   bitwarden/events:2022.6.2          "/entrypoint.sh"   2 minutes ago   Up 2 minutes (healthy)   5000/tcp                                                                                 bitwarden-events
898de4c5e4d2   bitwarden/attachments:2022.6.2     "/entrypoint.sh"   2 minutes ago   Up 2 minutes (healthy)                                                                                            bitwarden-attachments
9d022f01a65a   bitwarden/notifications:2022.6.2   "/entrypoint.sh"   2 minutes ago   Up 2 minutes (healthy)   5000/tcp                                                                                 bitwarden-notifications
ff78ab89feb2   bitwarden/web:2022.6.2             "/entrypoint.sh"   2 minutes ago   Up 2 minutes (healthy)                                                                                            bitwarden-web
2c9daf6c66fa   bitwarden/api:2022.6.2             "/entrypoint.sh"   2 minutes ago   Up 2 minutes (healthy)   5000/tcp                                                                                 bitwarden-api
22bc773477ca   bitwarden/icons:2022.6.2           "/entrypoint.sh"   2 minutes ago   Up 2 minutes (healthy)   5000/tcp                                                                                 bitwarden-icons

### Build Version

2022.6.2

### Environment

Self-Hosted

### Environment Details

- Operating System: RHEL 8.6
- Docker Engine: 20.10.17
- Docker Compose: 2.6.0
