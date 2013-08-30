using System;
using System.Threading;
using System.Threading.Tasks;

namespace FritzBoxApi {
    /// <summary>
    /// The class FritzBox provices quick access to a newly created for Fritz!OS 5.50 (or newer).
    /// </summary>
    public static class FritzBox {
        /// <summary>
        /// Asynchronously connect to Fritz!Box api at the host, authenticated only with given password
        /// </summary>
        /// <param name="host">The host address</param>
        /// <param name="password">The password</param>
        /// <param name="ct">The cancellation token used to cancel this operation</param>
        /// <returns>An api session</returns>
        public static async Task<Session> ConnectAsync(String host, String password, CancellationToken ct) {
            return await ConnectAsync(new Session(host, password), ct);
        }

        /// <summary>
        /// Asynchronously connect to Fritz!Box api at the host, authenticated only with a login credential consisting of a username / password combination
        /// </summary>
        /// <param name="host">The host address</param>
        /// <param name="username">The username</param>
        /// <param name="password">The password</param>
        /// <param name="ct">The cancellation token used to cancel this operation</param>
        /// <returns>An api session</returns>
        public static async Task<Session> ConnectAsync(String host, String username, String password, CancellationToken ct) {
             return await ConnectAsync(new Session(host, username, password), ct);
        }

        private static async Task<Session> ConnectAsync(Session session, CancellationToken ct) {
            await session.LoginAsync(ct);

            return session;
        }
    }
}