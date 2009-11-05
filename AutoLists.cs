using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using System.Net;
using System.Web;

using Misuzilla.Net.Irc;
using Misuzilla.Applications.TwitterIrcGateway;
using Misuzilla.Applications.TwitterIrcGateway.AddIns;
using Misuzilla.Applications.TwitterIrcGateway.AddIns.Console;

namespace Spica.Applications.TwitterIrcGateway.AddIns.AutoLists
{
	public class AutoListsMatchPatternConfiguration : IConfiguration
	{
		public bool Enabled { get; set; }
		public string Slug { get; set; }
		public string MatchPattern { get; set; }

		public AutoListsMatchPatternConfiguration()
		{
			Enabled = true;
			MatchPattern = String.Empty;
		}

		public bool IsMatch(Status status)
		{
			return Enabled && Regex.IsMatch(status.Text, MatchPattern);
		}

		public override string ToString()
		{
			return (Enabled ? String.Empty : "[DISABLED]")
				+ (String.IsNullOrEmpty(Slug) ? String.Empty : String.Format(" Slug={0}", Slug))
				+ (String.IsNullOrEmpty(MatchPattern) ? String.Empty : String.Format(" MatchPattern={0}", MatchPattern))
				;
		}
	}

	public class AutoListsConfiguration : IConfiguration
	{
		[Browsable(false)]
		public List<AutoListsMatchPatternConfiguration> Items { get; set; }
		public AutoListsConfiguration()
		{
			Items = new List<AutoListsMatchPatternConfiguration>();
		}
	}

	[Description("自動で Lists に追加する設定を行うコンテキストに切り替えます")]
	public class AutoListsContext : Context
	{
		private AutoListsAddIn AddIn { get { return CurrentSession.AddInManager.GetAddIn<AutoListsAddIn>(); } }
		private List<AutoListsMatchPatternConfiguration> Items { get { return AddIn.Configuration.Items; } }

		public override IConfiguration[] Configurations { get { return new IConfiguration[] { AddIn.Configuration }; } }
		protected override void OnConfigurationChanged(IConfiguration config, System.Reflection.MemberInfo memberInfo, object value)
		{
			if (config is AutoListsConfiguration)
			{
				AddIn.Configuration = config as AutoListsConfiguration;
				AddIn.SaveConfig();
			}
		}

		[Description("リストを新規追加します")]
		public void CreateList(string[] args)
		{
			if (args.Length == 0)
            {
                Console.NotifyMessage("エラー: リスト名が指定されていません。");
                return;
            }

			AddIn.CreateList(args[0], args.Length > 1 ? args[1] : "public");
		}

		#region List
		[Description("存在するパターンをすべて表示します")]
		public void List()
		{
			if (Items.Count == 0)
			{
				Console.NotifyMessage("パターンは現在設定されていません。");
				return;
			}

			for (Int32 i = 0; i < Items.Count; ++i)
			{
				var item = Items[i];
				Console.NotifyMessage(String.Format("{0}: {1}", i, item));
			}
		}

		[Description("指定したパターンを有効化します")]
		public void Enable(string arg)
		{
			SwitchEnable(arg, true);
		}

		[Description("指定したパターンを無効化します")]
		public void Disable(string arg)
		{
			SwitchEnable(arg, false);
		}

		[Description("指定したパターンを削除します")]
		public void Remove(string arg)
		{
			FindAt(arg, item =>
			{
				Items.Remove(item);
				AddIn.SaveConfig();
				Console.NotifyMessage(string.Format("パターン {0} を削除しました。", item));
			});
		}

		[Description("指定したパターンを編集します")]
		public void Edit(String arg)
		{
			FindAt(arg, item =>
			{
				var ctx = Console.GetContext<EditAutoListsContext>(CurrentServer, CurrentSession) as EditAutoListsContext;
				ctx.SetEditItem(Items, item);
				Console.PushContext(ctx);
			});
		}

		[Description("パターンを新規追加します")]
		public void New()
		{
			var ctx = Console.GetContext<EditAutoListsContext>(CurrentServer, CurrentSession) as EditAutoListsContext;
			ctx.SetNewItem(Items, new AutoListsMatchPatternConfiguration());
			Console.PushContext(ctx);
		}

		private void SwitchEnable(string arg, bool enable)
		{
			FindAt(arg, item =>
			{
				item.Enabled = enable;
				AddIn.SaveConfig();
				Console.NotifyMessage(String.Format("パターン {0} を{1}化しました。", item, (enable ? "有効" : "無効")));
			});
		}

		private void FindAt(String arg, Action<AutoListsMatchPatternConfiguration> action)
		{
			int index;
			if (int.TryParse(arg, out index))
			{
				if (index < Items.Count && index > -1)
				{
					action(Items[index]);
				}
				else
				{
					Console.NotifyMessage("存在しないパターンが指定されました。");
				}
			}
			else
			{
				Console.NotifyMessage("パターンの指定が正しくありません。");
			}
		}
		#endregion
	}

	public class EditAutoListsContext : Context
	{
		private AutoListsAddIn AddIn { get { return CurrentSession.AddInManager.GetAddIn<AutoListsAddIn>(); } }

		private List<AutoListsMatchPatternConfiguration> Items { get; set; }
		private AutoListsMatchPatternConfiguration Item { get; set; }
		private Boolean IsNew { get; set; }

		public override IConfiguration[] Configurations { get { return new IConfiguration[] { Item }; } }
		public override string ContextName { get { return (IsNew ? "New" : "Edit") + typeof(AutoListsContext).Name; } }

		[Browsable(false)]
		public void SetNewItem(List<AutoListsMatchPatternConfiguration> items, AutoListsMatchPatternConfiguration item)
		{
			Items = items;
			Item = item;
			IsNew = true;
		}

		[Browsable(false)]
		public void SetEditItem(List<AutoListsMatchPatternConfiguration> items, AutoListsMatchPatternConfiguration item)
		{
			Items = items;
			Item = item;
			IsNew = false;
		}

		[Description("パターンを保存してコンテキストを終了します")]
		public void Save()
		{
			if (IsNew)
				Items.Add(Item);

			AddIn.SaveConfig();
			Console.NotifyMessage(string.Format("パターンを{0}しました。", (IsNew ? "新規作成" : "保存")));
			Exit();
		}
	}

	public class AutoListsAddIn : AddInBase
	{
		public AutoListsConfiguration Configuration { get; internal set; }
		public Dictionary<string, List<int>> ListsMembers { get; set; }

		public AutoListsAddIn()
		{
			ListsMembers = new Dictionary<string, List<int>>();
		}

		public override void Initialize()
		{
			Configuration = CurrentSession.AddInManager.GetConfig<AutoListsConfiguration>();
			CurrentSession.PreProcessTimelineStatuses += new EventHandler<TimelineStatusesEventArgs>(CurrentSession_PreProcessTimelineStatuses);
			CurrentSession.AddInsLoadCompleted += (sender, e) => CurrentSession.AddInManager.GetAddIn<ConsoleAddIn>().RegisterContext<AutoListsContext>();
		}

		internal void SaveConfig()
		{
			CurrentSession.AddInManager.SaveConfig(Configuration);
		}

		private void CurrentSession_PreProcessTimelineStatuses(object sender, TimelineStatusesEventArgs e)
		{
			if (e.IsFirstTime)
				return;

			foreach (var status in e.Statuses.Status)
			{
				foreach (var item in Configuration.Items)
				{
					if (!ListsMembers.ContainsKey(item.Slug))
						ListsMembers.Add(item.Slug, new List<int>());

					if (item.IsMatch(status))
					{
						List<int> members = ListsMembers[item.Slug];
						if (!members.Contains(status.User.Id))
						{
							members.Add(status.User.Id);

							// 適当に非同期で投げまくる
							Action<AutoListsMatchPatternConfiguration, Status> action = this.AddMember;
							action.BeginInvoke(item, status, new AsyncCallback((r) => { (r.AsyncState as Action<AutoListsMatchPatternConfiguration, Status>).EndInvoke(r); }), action);
						}
					}
				}
			}
		}



		/// <summary>
		/// メッセージを送信する
		/// </summary>
		/// <param name="message"></param>
		private void SendMessage(String message)
		{
#if true
			var console = CurrentSession.AddInManager.GetAddIn<ConsoleAddIn>();
			console.NotifyMessage(GetType().Name, message);
#else
			CurrentSession.SendTwitterGatewayServerMessage(String.Format("{0}: {1}", GetType().Name, message));
#endif
		}

		/// <summary>
		/// リストにメンバーを追加する
		/// </summary>
		internal void AddMember(AutoListsMatchPatternConfiguration item, Status status)
		{
			try
			{
				if (!IsExist(item, status))
				{
					String url = String.Format("/{0}/{1}/members.xml?id={2}", CurrentSession.TwitterUser.ScreenName, item.Slug, status.User.Id);
					String body = CurrentSession.TwitterService.POST(url, new byte[] { });
					SendMessage(String.Format("リスト {0} に {1} を追加しました。", item.Slug, status.User.ScreenName));
				}
			}
			catch (Exception ex)
			{
				SendMessage(ex.Message);
			}
		}

		/// <summary>
		/// メンバーがリストに存在するか
		/// </summary>
		internal Boolean IsExist(AutoListsMatchPatternConfiguration item, Status status)
		{
			try
			{
				String url = String.Format("/{0}/{1}/members/{2}.xml", CurrentSession.TwitterUser.ScreenName, item.Slug, status.User.Id);
				String body = CurrentSession.TwitterService.GET(url, false);
				return true;
			}
			catch (Exception)
			{
				// 404なら例外を投げるっぽい
				return false;
			}
		}

		/// <summary>
		/// リストを新規作成する
		/// </summary>
		internal void CreateList(string name, string mode)
		{
			try
			{
				String url = String.Format("/{0}/lists.xml?name={1}&mode={2}", CurrentSession.TwitterUser.ScreenName, name, mode);
				String body = CurrentSession.TwitterService.POST(url, new byte[] { });
				SendMessage(String.Format("リスト {0} ({1}) を作成しました。", name, mode));
			}
			catch (Exception ex)
			{
				SendMessage(ex.Message);
			}
		}
	}
}
