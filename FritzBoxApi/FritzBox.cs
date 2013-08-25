using System;
using System.Threading;
using System.Threading.Tasks;

namespace FritzBoxApi {
    /// <summary>
    /// The class FritzBox provices quick access to a newly created session.
    /// </summary>
    public static class FritzBox {
        /// <summary>
        /// Asynchronously connect to Fritz!Box api at the host, authenticated with given password
        /// </summary>
        /// <param name="host"></param>
        /// <param name="password"></param>
        /// <returns></returns>
        public static async Task<Session> ConnectAsync(String host, String password, CancellationToken ct) {
            var session = new Session(host, password);

            ct.ThrowIfCancellationRequested();

            await session.LoginAsync(ct);

            return session;
        }

        /*
        public static Session Connect(String host, String password) {
            return ConnectAsync(host, password).Result;
        }
        */
    }
}