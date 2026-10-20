// No-op stand-ins for the Google Sign-In native plugin. The real plugin
// (Plugins/iOS/GoogleSignIn) needs the legacy GoogleSignIn SDK 5.x pod, which
// conflicts with Firebase 12, so its iOS platform is disabled in the import
// settings. When re-enabling it, delete this file. GoogleSignInImpl.cs still declares
// these [DllImport("__Internal")] symbols, so IL2CPP needs them to link.
// FirebaseManager never calls them on iOS while GOOGLE_SIGN_IN is undefined.

#include <stddef.h>

extern "C" {

void *GoogleSignIn_Create(void *data) { return NULL; }
void GoogleSignIn_EnableDebugLogging(void *self, bool flag) {}
bool GoogleSignIn_Configure(void *self, bool useGameSignIn,
                            const char *webClientId, bool requestAuthCode,
                            bool forceTokenRefresh, bool requestEmail,
                            bool requestIdToken, bool hidePopups,
                            const char **additionalScopes, int scopeCount,
                            const char *accountName) {
  return false;
}
void *GoogleSignIn_SignIn(void *self) { return NULL; }
void *GoogleSignIn_SignInSilently(void *self) { return NULL; }
void GoogleSignIn_Signout(void *self) {}
void GoogleSignIn_Disconnect(void *self) {}
void GoogleSignIn_DisposeFuture(void *self) {}
bool GoogleSignIn_Pending(void *self) { return false; }
void *GoogleSignIn_Result(void *self) { return NULL; }
int GoogleSignIn_Status(void *self) { return -1; }
size_t GoogleSignIn_GetServerAuthCode(void *self, char *buf, size_t len) { return 0; }
size_t GoogleSignIn_GetDisplayName(void *self, char *buf, size_t len) { return 0; }
size_t GoogleSignIn_GetEmail(void *self, char *buf, size_t len) { return 0; }
size_t GoogleSignIn_GetFamilyName(void *self, char *buf, size_t len) { return 0; }
size_t GoogleSignIn_GetGivenName(void *self, char *buf, size_t len) { return 0; }
size_t GoogleSignIn_GetIdToken(void *self, char *buf, size_t len) { return 0; }
size_t GoogleSignIn_GetImageUrl(void *self, char *buf, size_t len) { return 0; }
size_t GoogleSignIn_GetUserId(void *self, char *buf, size_t len) { return 0; }

}  // extern "C"
