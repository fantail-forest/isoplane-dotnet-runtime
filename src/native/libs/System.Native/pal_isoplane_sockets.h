// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#pragma once

#include <sys/socket.h>
#include <sys/types.h>

// Isoplane's socket functions, defined by the platform layer every Isoplane
// guest links. libc does not expose the host resource behind a descriptor;
// these keep it, so TCP_NODELAY reaches the host, IPV6_V6ONLY and its refusal
// of IPv4-mapped destinations are honoured, and a failed send reports the
// host's error code. Each has the contract of the libc function it stands in
// for.
int isoplane_socket_open(int domain, int type, int protocol);
int isoplane_socket_close(int fd);
int isoplane_socket_connect(int fd, const struct sockaddr* address, socklen_t length);
int isoplane_socket_get_option(int fd, int level, int option, void* value, socklen_t* length);
int isoplane_socket_set_option(int fd, int level, int option, const void* value, socklen_t length);
ssize_t isoplane_socket_send(int fd, const void* buffer, size_t length, int flags);
