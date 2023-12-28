#!/bin/bash

echo "---Ensuring UID: ${UID} matches user---"
usermod -u ${UID} ${USER}
echo "---Ensuring GID: ${GID} matches user---"
groupmod -g ${GID} ${USER} > /dev/null 2>&1 ||:
usermod -g ${GID} ${USER}
echo "---Setting umask to ${UMASK}---"
umask ${UMASK}

echo "---Taking ownership of data...---"
chown -R root:${GID} /opt/scripts
chmod -R 750 /opt/scripts

echo "---Permissions...---"
chown -R ${UID}:${GID} ${DATA_DIR}

# TODO - figure out how to deal with auto updating
echo "---Updating...---"
su ${USER} -c "/opt/scripts/update.sh"

su ${USER} -c "/opt/scripts/server.sh"