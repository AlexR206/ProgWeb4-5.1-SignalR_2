using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using signalr.backend.Data;
using signalr.backend.Models;

namespace signalr.backend.Hubs
{
    // Cette classe sert à garder une trace des connexions actives entre les utilisateurs et leur identifiant SignalR.
    // ➜ La clé (string) représente habituellement l’email de l’utilisateur.
    // ➜ La valeur (string) représente l’identifiant unique de connexion (UserIdentifier).
    // Ce dictionnaire permet de retrouver facilement quelle connexion appartient à quel utilisateur.
    // Par exemple : UserConnections["alex@gmail.com"] = "13f53bde-9e42-43a0-b9a1-0f1b1d2d8c53"
    public static class UserHandler
    {
        // Un Dictionary est une collection de paires clé/valeur.
        // Ici: clé = email de l’utilisateur, valeur = UserIdentifier (chaîne unique fournie par SignalR)
        public static Dictionary<string, string> UserConnections { get; set; } = new Dictionary<string, string>();
    }

    // [Authorize] veut dire que seulement les utilisateurs connectés (authentifiés) peuvent accéder au Hub.
    // C’est la même logique que pour les contrôleurs Web API.
    [Authorize]
    // Le Hub agit comme un "contrôleur" temps réel. Chaque client connecté parle avec ce Hub.
    public class ChatHub : Hub
    {
        public ApplicationDbContext _context;

        // Cette propriété retourne l’utilisateur actuellement connecté (IdentityUser).
        // Elle se base sur le UserIdentifier envoyé automatiquement par SignalR à partir du cookie ou du token.
        public IdentityUser CurentUser
        {
            get
            {
                // TODO on récupère le userid à partir du Cookie qui devrait être envoyé automatiquement
                // Context.UserIdentifier = identifiant unique (souvent le même que l’Id de l’utilisateur dans la BD)
                string userid = Context.UserIdentifier!;

                // On va chercher dans la base de données l’utilisateur correspondant à cet Id
                var user = _context.Users.Single(u => u.Id == userid);

                // On retourne l’objet IdentityUser complet (avec Email, UserName, etc.)
                return user;
            }
        }

        // Le constructeur reçoit le contexte EF Core pour accéder à la BD
        public ChatHub(ApplicationDbContext context)
        {
            _context = context;
        }

        // Méthode appelée automatiquement quand un client se connecte au Hub
        public async override Task OnConnectedAsync()
        {
            // Lorsqu’un utilisateur se connecte, on le fait rejoindre le "chat général"
            await JoinChat();
        }

        // Méthode appelée automatiquement quand un client se déconnecte
        public async override Task OnDisconnectedAsync(Exception? exception)
        {
            // TODO Lors de la fermeture de la connexion, on met à jour notre dictionnary d'utilisateurs connectés
            // On cherche dans le dictionnaire l’entrée correspondant à cette connexion (grâce au UserIdentifier)
            KeyValuePair<string, string> entrie = UserHandler.UserConnections.SingleOrDefault(uc => uc.Value == Context.UserIdentifier);

            // On supprime l’utilisateur du dictionnaire (il n’est plus connecté)
            UserHandler.UserConnections.Remove(entrie.Key);

            // Après suppression, on envoie la nouvelle liste d’utilisateurs à tout le monde pour les mettre à jour
            await UserList();
        }

        private async Task JoinChat()
        {
            // TODO Context.ConnectionId est l'identifiant de la connexion entre le web socket et l'utilisateur
            // Chaque onglet de navigateur connecté a un ConnectionId unique.
            // Ce sera utile pour ajouter/retirer un utilisateur de groupes (par exemple les channels).

            // On ajoute l’utilisateur dans le dictionnaire des connexions actives
            // Clé = email, valeur = UserIdentifier (lien permanent entre l’utilisateur et sa connexion)
            UserHandler.UserConnections.Add(CurentUser.Email!, Context.UserIdentifier);

            // On envoie la liste complète des utilisateurs connectés à tous les clients
            await UserList();

            // On envoie aussi la liste complète des channels disponibles uniquement au client qui vient de se connecter
            await Clients.Caller.SendAsync("ChannelsList", _context.Channel.ToList());
        }

        // Crée un nouveau canal et le sauvegarde dans la base de données
        public async Task CreateChannel(string title)
        {
            // On ajoute un nouveau Channel dans la BD
            _context.Channel.Add(new Channel { Title = title });
            await _context.SaveChangesAsync();

            // Après création, on envoie la nouvelle liste de canaux à tous les utilisateurs connectés
            await Clients.All.SendAsync("ChannelsList", await _context.Channel.ToListAsync());
        }

        // Supprime un canal
        public async Task DeleteChannel(int channelId)
        {
            Channel channel = _context.Channel.Find(channelId);

            if (channel != null)
            {
                _context.Channel.Remove(channel);
                await _context.SaveChangesAsync();
            }

            // Chaque canal correspond à un "groupe" SignalR
            // Par exemple, Channel #2 → "Channel2"
            string groupName = CreateChannelGroupName(channelId);

            // On envoie un message dans le groupe pour avertir que le canal a été détruit
            await Clients.Group(groupName).SendAsync("NewMessage", "[" + channel.Title + "] a été détruit");

            // On envoie aussi un signal aux membres du canal pour qu’ils le quittent (événement LeaveChannel)
            await Clients.Group(groupName).SendAsync("LeaveChannel");

            // Enfin, on met à jour la liste de canaux pour tous les clients
            await Clients.All.SendAsync("ChannelsList", await _context.Channel.ToListAsync());
        }

        // Envoie à tous les clients la liste d’utilisateurs actuellement connectés
        public async Task UserList()
        {
            // TODO On envoie un évènement de type UserList à tous les Utilisateurs
            // TODO On peut envoyer en paramètre tous les types que l'on veut,
            // ici UserHandler.UserConnections.Keys correspond à la liste de tous les emails des utilisateurs connectés.
            // Chaque client Angular écoute cet évènement et met à jour sa liste d’utilisateurs affichée à l’écran.
            await Clients.All.SendAsync("UsersList", UserHandler.UserConnections.ToList());
        }

        // Permet à un utilisateur de quitter un canal et d’en rejoindre un autre
        public async Task JoinChannel(int oldChannelId, int newChannelId)
        {
            string userTag = "[" + CurentUser.Email! + "]";

            // Si l’utilisateur était déjà dans un canal, on l’en retire
            if (oldChannelId > 0)
            {
                string oldGroupName = CreateChannelGroupName(oldChannelId);
                Channel channel = _context.Channel.Find(oldChannelId);

                // On envoie un message dans l’ancien canal pour dire qu’il quitte
                string message = userTag + " quitte: " + channel.Title;
                await Clients.Group(oldGroupName).SendAsync("NewMessage", message);

                // On retire cette connexion du groupe correspondant à l’ancien canal
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, oldGroupName);
            }

            // S’il rejoint un nouveau canal
            if (newChannelId > 0)
            {
                string newGroupName = CreateChannelGroupName(newChannelId);

                // On ajoute cette connexion au nouveau groupe
                await Groups.AddToGroupAsync(Context.ConnectionId, newGroupName);

                // On envoie un message à tous les membres de ce groupe
                Channel channel = _context.Channel.Find(newChannelId);
                string message = userTag + " a rejoint : " + channel.Title;
                await Clients.Group(newGroupName).SendAsync("NewMessage", message);
            }
        }

        // Envoie un message à un utilisateur spécifique, un canal spécifique, ou à tout le monde
        public async Task SendMessage(string message, int channelId, string userId)
        {
            if (userId != null)
            {
                // Si on a un userId → message privé
                string messageWithTag = "[De: " + CurentUser.Email! + "] " + message;
                // Clients.User(userId) permet d’envoyer le message seulement à l’utilisateur avec cet identifiant
                await Clients.User(userId).SendAsync("NewMessage", messageWithTag);
            }
            else if (channelId != 0)
            {
                // Si on a un channelId → message dans un canal spécifique
                string groupName = CreateChannelGroupName(channelId);
                Channel channel = _context.Channel.Find(channelId);

                // Tous les utilisateurs dans le groupe "ChannelX" recevront ce message
                await Clients.Group(groupName).SendAsync("NewMessage", "[" + channel.Title + "] " + message);
            }
            else
            {
                // Sinon → message global envoyé à tous les clients connectés
                await Clients.All.SendAsync("NewMessage", "[Tous] " + message);
            }
        }

        // Génère le nom d’un groupe à partir de l’ID du canal (ex: 1 → "Channel1")
        private static string CreateChannelGroupName(int channelId)
        {
            return "Channel" + channelId;
        }
    }
}
