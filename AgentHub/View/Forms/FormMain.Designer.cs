
namespace AgentHub.View.Forms
{
partial class FormMain
    {
        /// <summary>
        /// 필수 디자이너 변수입니다.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// 사용 중인 모든 리소스를 정리합니다.
        /// </summary>
        /// <param name="disposing">관리되는 리소스를 삭제해야 하면 true이고, 그렇지 않으면 false입니다.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form 디자이너에서 생성한 코드

        /// <summary>
        /// 디자이너 지원에 필요한 메서드입니다. 
        /// 이 메서드의 내용을 코드 편집기로 수정하지 마세요.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(FormMain));
            this.pnlResizeBorderTop = new System.Windows.Forms.Panel();
            this.pnlResizeBorderBottom = new System.Windows.Forms.Panel();
            this.pnlResizeBorderLeft = new System.Windows.Forms.Panel();
            this.pnlResizeBorderRight = new System.Windows.Forms.Panel();
            this.pnlCenter = new System.Windows.Forms.Panel();
            this.pnlMain = new System.Windows.Forms.Panel();
            this.pnlMainCenter = new System.Windows.Forms.Panel();
            this.webViewServer = new Microsoft.Web.WebView2.WinForms.WebView2();
            this.pnlTitleBar = new System.Windows.Forms.Panel();
            this.flpTitle = new System.Windows.Forms.FlowLayoutPanel();
            this.lblVersionInfo = new System.Windows.Forms.Label();
            this.lblServerInfo = new System.Windows.Forms.Label();
            this.lblTitle = new System.Windows.Forms.Label();
            this.btnWindowMinimize = new System.Windows.Forms.Button();
            this.btnWindowClose = new System.Windows.Forms.Button();
            this.btnWindowMaximize = new System.Windows.Forms.Button();
            this.btnWindowRestore = new System.Windows.Forms.Button();
            this.pnlCenter.SuspendLayout();
            this.pnlMain.SuspendLayout();
            this.pnlMainCenter.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.webViewServer)).BeginInit();
            this.pnlTitleBar.SuspendLayout();
            this.flpTitle.SuspendLayout();
            this.SuspendLayout();
            // 
            // pnlResizeBorderTop
            // 
            this.pnlResizeBorderTop.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(48)))));
            this.pnlResizeBorderTop.Dock = System.Windows.Forms.DockStyle.Top;
            this.pnlResizeBorderTop.Location = new System.Drawing.Point(0, 0);
            this.pnlResizeBorderTop.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.pnlResizeBorderTop.Name = "pnlResizeBorderTop";
            this.pnlResizeBorderTop.Size = new System.Drawing.Size(1429, 4);
            this.pnlResizeBorderTop.TabIndex = 0;
            this.pnlResizeBorderTop.MouseDown += new System.Windows.Forms.MouseEventHandler(this.pnlResizeBorderTop_MouseDown);
            this.pnlResizeBorderTop.MouseLeave += new System.EventHandler(this.pnlResizeBorder_MouseLeave);
            this.pnlResizeBorderTop.MouseHover += new System.EventHandler(this.pnlResizeBorderTop_MouseHover);
            this.pnlResizeBorderTop.MouseMove += new System.Windows.Forms.MouseEventHandler(this.pnlResizeBorderTop_MouseMove);
            this.pnlResizeBorderTop.MouseUp += new System.Windows.Forms.MouseEventHandler(this.pnlResizeBorderTop_MouseUp);
            // 
            // pnlResizeBorderBottom
            // 
            this.pnlResizeBorderBottom.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(48)))));
            this.pnlResizeBorderBottom.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.pnlResizeBorderBottom.Location = new System.Drawing.Point(0, 1196);
            this.pnlResizeBorderBottom.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.pnlResizeBorderBottom.Name = "pnlResizeBorderBottom";
            this.pnlResizeBorderBottom.Size = new System.Drawing.Size(1429, 4);
            this.pnlResizeBorderBottom.TabIndex = 1;
            this.pnlResizeBorderBottom.MouseDown += new System.Windows.Forms.MouseEventHandler(this.pnlResizeBorderBottom_MouseDown);
            this.pnlResizeBorderBottom.MouseLeave += new System.EventHandler(this.pnlResizeBorder_MouseLeave);
            this.pnlResizeBorderBottom.MouseHover += new System.EventHandler(this.pnlResizeBorderBottom_MouseHover);
            this.pnlResizeBorderBottom.MouseMove += new System.Windows.Forms.MouseEventHandler(this.pnlResizeBorderBottom_MouseMove);
            this.pnlResizeBorderBottom.MouseUp += new System.Windows.Forms.MouseEventHandler(this.pnlResizeBorderBottom_MouseUp);
            // 
            // pnlResizeBorderLeft
            // 
            this.pnlResizeBorderLeft.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(48)))));
            this.pnlResizeBorderLeft.Dock = System.Windows.Forms.DockStyle.Left;
            this.pnlResizeBorderLeft.Location = new System.Drawing.Point(0, 4);
            this.pnlResizeBorderLeft.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.pnlResizeBorderLeft.Name = "pnlResizeBorderLeft";
            this.pnlResizeBorderLeft.Size = new System.Drawing.Size(4, 1192);
            this.pnlResizeBorderLeft.TabIndex = 1;
            this.pnlResizeBorderLeft.MouseDown += new System.Windows.Forms.MouseEventHandler(this.pnlResizeBorderLeft_MouseDown);
            this.pnlResizeBorderLeft.MouseLeave += new System.EventHandler(this.pnlResizeBorder_MouseLeave);
            this.pnlResizeBorderLeft.MouseHover += new System.EventHandler(this.pnlResizeBorderLeft_MouseHover);
            this.pnlResizeBorderLeft.MouseMove += new System.Windows.Forms.MouseEventHandler(this.pnlResizeBorderLeft_MouseMove);
            this.pnlResizeBorderLeft.MouseUp += new System.Windows.Forms.MouseEventHandler(this.pnlResizeBorderLeft_MouseUp);
            // 
            // pnlResizeBorderRight
            // 
            this.pnlResizeBorderRight.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(48)))));
            this.pnlResizeBorderRight.Dock = System.Windows.Forms.DockStyle.Right;
            this.pnlResizeBorderRight.Location = new System.Drawing.Point(1425, 4);
            this.pnlResizeBorderRight.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.pnlResizeBorderRight.Name = "pnlResizeBorderRight";
            this.pnlResizeBorderRight.Size = new System.Drawing.Size(4, 1192);
            this.pnlResizeBorderRight.TabIndex = 1;
            this.pnlResizeBorderRight.MouseDown += new System.Windows.Forms.MouseEventHandler(this.pnlResizeBorderRight_MouseDown);
            this.pnlResizeBorderRight.MouseLeave += new System.EventHandler(this.pnlResizeBorder_MouseLeave);
            this.pnlResizeBorderRight.MouseHover += new System.EventHandler(this.pnlResizeBorderRight_MouseHover);
            this.pnlResizeBorderRight.MouseMove += new System.Windows.Forms.MouseEventHandler(this.pnlResizeBorderRight_MouseMove);
            this.pnlResizeBorderRight.MouseUp += new System.Windows.Forms.MouseEventHandler(this.pnlResizeBorderRight_MouseUp);
            // 
            // pnlCenter
            // 
            this.pnlCenter.Controls.Add(this.pnlMain);
            this.pnlCenter.Controls.Add(this.pnlTitleBar);
            this.pnlCenter.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pnlCenter.Location = new System.Drawing.Point(4, 4);
            this.pnlCenter.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.pnlCenter.Name = "pnlCenter";
            this.pnlCenter.Size = new System.Drawing.Size(1421, 1192);
            this.pnlCenter.TabIndex = 2;
            // 
            // pnlMain
            // 
            this.pnlMain.BackColor = System.Drawing.Color.Transparent;
            this.pnlMain.Controls.Add(this.pnlMainCenter);
            this.pnlMain.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pnlMain.Location = new System.Drawing.Point(0, 48);
            this.pnlMain.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.pnlMain.Name = "pnlMain";
            this.pnlMain.Size = new System.Drawing.Size(1421, 1144);
            this.pnlMain.TabIndex = 1;
            // 
            // pnlMainCenter
            // 
            this.pnlMainCenter.Controls.Add(this.webViewServer);
            this.pnlMainCenter.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pnlMainCenter.Location = new System.Drawing.Point(0, 0);
            this.pnlMainCenter.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.pnlMainCenter.Name = "pnlMainCenter";
            this.pnlMainCenter.Size = new System.Drawing.Size(1421, 1144);
            this.pnlMainCenter.TabIndex = 2;
            // 
            // webViewServer
            // 
            this.webViewServer.AllowExternalDrop = true;
            this.webViewServer.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(19)))), ((int)(((byte)(20)))), ((int)(((byte)(30)))));
            this.webViewServer.CreationProperties = null;
            this.webViewServer.DefaultBackgroundColor = System.Drawing.Color.White;
            this.webViewServer.Dock = System.Windows.Forms.DockStyle.Fill;
            this.webViewServer.Location = new System.Drawing.Point(0, 0);
            this.webViewServer.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.webViewServer.Name = "webViewServer";
            this.webViewServer.Size = new System.Drawing.Size(1421, 1144);
            this.webViewServer.TabIndex = 1;
            this.webViewServer.ZoomFactor = 1D;
            // 
            // pnlTitleBar
            // 
            this.pnlTitleBar.BackColor = System.Drawing.Color.Transparent;
            this.pnlTitleBar.Controls.Add(this.flpTitle);
            this.pnlTitleBar.Controls.Add(this.lblTitle);
            this.pnlTitleBar.Controls.Add(this.btnWindowMinimize);
            this.pnlTitleBar.Controls.Add(this.btnWindowClose);
            this.pnlTitleBar.Controls.Add(this.btnWindowMaximize);
            this.pnlTitleBar.Controls.Add(this.btnWindowRestore);
            this.pnlTitleBar.Dock = System.Windows.Forms.DockStyle.Top;
            this.pnlTitleBar.Location = new System.Drawing.Point(0, 0);
            this.pnlTitleBar.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.pnlTitleBar.Name = "pnlTitleBar";
            this.pnlTitleBar.Size = new System.Drawing.Size(1421, 48);
            this.pnlTitleBar.TabIndex = 0;
            this.pnlTitleBar.MouseDown += new System.Windows.Forms.MouseEventHandler(this.pnlTitleBar_MouseDown);
            // 
            // flpTitle
            // 
            this.flpTitle.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.flpTitle.BackColor = System.Drawing.Color.Transparent;
            this.flpTitle.Controls.Add(this.lblVersionInfo);
            this.flpTitle.Controls.Add(this.lblServerInfo);
            this.flpTitle.Location = new System.Drawing.Point(137, 0);
            this.flpTitle.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.flpTitle.Name = "flpTitle";
            this.flpTitle.Padding = new System.Windows.Forms.Padding(0, 12, 0, 12);
            this.flpTitle.Size = new System.Drawing.Size(1091, 48);
            this.flpTitle.TabIndex = 11;
            this.flpTitle.MouseDown += new System.Windows.Forms.MouseEventHandler(this.pnlTitleBar_MouseDown);
            // 
            // lblVersionInfo
            // 
            this.lblVersionInfo.AutoSize = true;
            this.lblVersionInfo.Font = new System.Drawing.Font("맑은 고딕", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(129)));
            this.lblVersionInfo.ForeColor = System.Drawing.Color.LightCoral;
            this.lblVersionInfo.Location = new System.Drawing.Point(0, 12);
            this.lblVersionInfo.Margin = new System.Windows.Forms.Padding(0, 0, 7, 0);
            this.lblVersionInfo.Name = "lblVersionInfo";
            this.lblVersionInfo.Size = new System.Drawing.Size(84, 25);
            this.lblVersionInfo.TabIndex = 10;
            this.lblVersionInfo.Text = "버전정보";
            this.lblVersionInfo.MouseDown += new System.Windows.Forms.MouseEventHandler(this.pnlTitleBar_MouseDown);
            // 
            // lblServerInfo
            // 
            this.lblServerInfo.AutoSize = true;
            this.lblServerInfo.Font = new System.Drawing.Font("맑은 고딕", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(129)));
            this.lblServerInfo.ForeColor = System.Drawing.Color.SandyBrown;
            this.lblServerInfo.Location = new System.Drawing.Point(91, 12);
            this.lblServerInfo.Margin = new System.Windows.Forms.Padding(0, 0, 7, 0);
            this.lblServerInfo.Name = "lblServerInfo";
            this.lblServerInfo.Size = new System.Drawing.Size(84, 25);
            this.lblServerInfo.TabIndex = 9;
            this.lblServerInfo.Text = "서버정보";
            this.lblServerInfo.MouseDown += new System.Windows.Forms.MouseEventHandler(this.pnlTitleBar_MouseDown);
            // 
            // lblTitle
            // 
            this.lblTitle.AutoSize = true;
            this.lblTitle.Font = new System.Drawing.Font("맑은 고딕", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(129)));
            this.lblTitle.ForeColor = System.Drawing.Color.WhiteSmoke;
            this.lblTitle.Location = new System.Drawing.Point(11, 12);
            this.lblTitle.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lblTitle.Name = "lblTitle";
            this.lblTitle.Size = new System.Drawing.Size(126, 25);
            this.lblTitle.TabIndex = 8;
            this.lblTitle.Text = "에이전트 허브";
            this.lblTitle.MouseDown += new System.Windows.Forms.MouseEventHandler(this.pnlTitleBar_MouseDown);
            // 
            // btnWindowMinimize
            // 
            this.btnWindowMinimize.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnWindowMinimize.BackColor = System.Drawing.Color.Transparent;
            this.btnWindowMinimize.BackgroundImageLayout = System.Windows.Forms.ImageLayout.None;
            this.btnWindowMinimize.FlatAppearance.BorderSize = 0;
            this.btnWindowMinimize.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(63)))), ((int)(((byte)(63)))), ((int)(((byte)(65)))));
            this.btnWindowMinimize.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnWindowMinimize.Image = global::AgentHub.Properties.Resources.window_minimize;
            this.btnWindowMinimize.Location = new System.Drawing.Point(1241, 0);
            this.btnWindowMinimize.Margin = new System.Windows.Forms.Padding(7, 8, 7, 8);
            this.btnWindowMinimize.Name = "btnWindowMinimize";
            this.btnWindowMinimize.Size = new System.Drawing.Size(57, 48);
            this.btnWindowMinimize.TabIndex = 6;
            this.btnWindowMinimize.TabStop = false;
            this.btnWindowMinimize.UseVisualStyleBackColor = false;
            this.btnWindowMinimize.Click += new System.EventHandler(this.btnWindowMinimize_Click);
            // 
            // btnWindowClose
            // 
            this.btnWindowClose.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnWindowClose.BackColor = System.Drawing.Color.Transparent;
            this.btnWindowClose.BackgroundImageLayout = System.Windows.Forms.ImageLayout.None;
            this.btnWindowClose.FlatAppearance.BorderSize = 0;
            this.btnWindowClose.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(192)))), ((int)(((byte)(0)))), ((int)(((byte)(0)))));
            this.btnWindowClose.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnWindowClose.Image = global::AgentHub.Properties.Resources.window_close;
            this.btnWindowClose.Location = new System.Drawing.Point(1364, 0);
            this.btnWindowClose.Margin = new System.Windows.Forms.Padding(7, 8, 7, 8);
            this.btnWindowClose.Name = "btnWindowClose";
            this.btnWindowClose.Size = new System.Drawing.Size(57, 48);
            this.btnWindowClose.TabIndex = 1;
            this.btnWindowClose.TabStop = false;
            this.btnWindowClose.UseVisualStyleBackColor = false;
            this.btnWindowClose.Click += new System.EventHandler(this.btnWindowClose_Click);
            // 
            // btnWindowMaximize
            // 
            this.btnWindowMaximize.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnWindowMaximize.BackColor = System.Drawing.Color.Transparent;
            this.btnWindowMaximize.BackgroundImageLayout = System.Windows.Forms.ImageLayout.None;
            this.btnWindowMaximize.FlatAppearance.BorderSize = 0;
            this.btnWindowMaximize.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(63)))), ((int)(((byte)(63)))), ((int)(((byte)(65)))));
            this.btnWindowMaximize.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnWindowMaximize.Image = global::AgentHub.Properties.Resources.window_maximize;
            this.btnWindowMaximize.Location = new System.Drawing.Point(1302, 0);
            this.btnWindowMaximize.Margin = new System.Windows.Forms.Padding(7, 8, 7, 8);
            this.btnWindowMaximize.Name = "btnWindowMaximize";
            this.btnWindowMaximize.Size = new System.Drawing.Size(57, 48);
            this.btnWindowMaximize.TabIndex = 4;
            this.btnWindowMaximize.TabStop = false;
            this.btnWindowMaximize.UseVisualStyleBackColor = false;
            this.btnWindowMaximize.Click += new System.EventHandler(this.btnWindowMaximize_Click);
            // 
            // btnWindowRestore
            // 
            this.btnWindowRestore.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnWindowRestore.BackColor = System.Drawing.Color.Transparent;
            this.btnWindowRestore.BackgroundImageLayout = System.Windows.Forms.ImageLayout.None;
            this.btnWindowRestore.FlatAppearance.BorderSize = 0;
            this.btnWindowRestore.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(63)))), ((int)(((byte)(63)))), ((int)(((byte)(65)))));
            this.btnWindowRestore.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnWindowRestore.Image = global::AgentHub.Properties.Resources.window_restore;
            this.btnWindowRestore.Location = new System.Drawing.Point(1302, 0);
            this.btnWindowRestore.Margin = new System.Windows.Forms.Padding(7, 8, 7, 8);
            this.btnWindowRestore.Name = "btnWindowRestore";
            this.btnWindowRestore.Size = new System.Drawing.Size(57, 48);
            this.btnWindowRestore.TabIndex = 5;
            this.btnWindowRestore.TabStop = false;
            this.btnWindowRestore.UseVisualStyleBackColor = false;
            this.btnWindowRestore.Click += new System.EventHandler(this.btnWindowRestore_Click);
            // 
            // FormMain
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(10F, 18F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(19)))), ((int)(((byte)(20)))), ((int)(((byte)(30)))));
            this.ClientSize = new System.Drawing.Size(1429, 1200);
            this.ControlBox = false;
            this.Controls.Add(this.pnlCenter);
            this.Controls.Add(this.pnlResizeBorderLeft);
            this.Controls.Add(this.pnlResizeBorderRight);
            this.Controls.Add(this.pnlResizeBorderTop);
            this.Controls.Add(this.pnlResizeBorderBottom);
            this.DoubleBuffered = true;
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.MinimumSize = new System.Drawing.Size(1429, 1200);
            this.Name = "FormMain";
            this.Opacity = 0D;
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
            this.Text = "에이전트 허브";
            this.Load += new System.EventHandler(this.FormMain_Load);
            this.pnlCenter.ResumeLayout(false);
            this.pnlMain.ResumeLayout(false);
            this.pnlMainCenter.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.webViewServer)).EndInit();
            this.pnlTitleBar.ResumeLayout(false);
            this.pnlTitleBar.PerformLayout();
            this.flpTitle.ResumeLayout(false);
            this.flpTitle.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Panel pnlResizeBorderTop;
        private System.Windows.Forms.Panel pnlResizeBorderBottom;
        private System.Windows.Forms.Panel pnlResizeBorderLeft;
        private System.Windows.Forms.Panel pnlResizeBorderRight;
        private System.Windows.Forms.Panel pnlCenter;
        private System.Windows.Forms.Panel pnlTitleBar;
        private System.Windows.Forms.Button btnWindowClose;
        private System.Windows.Forms.Button btnWindowMinimize;
        private System.Windows.Forms.Button btnWindowMaximize;
        private System.Windows.Forms.Button btnWindowRestore;
        private System.Windows.Forms.Panel pnlMain;
        private System.Windows.Forms.Label lblTitle;
        private System.Windows.Forms.Label lblServerInfo;
        private System.Windows.Forms.FlowLayoutPanel flpTitle;
        private System.Windows.Forms.Label lblVersionInfo;
        private System.Windows.Forms.Panel pnlMainCenter;
        private Microsoft.Web.WebView2.WinForms.WebView2 webViewServer;
    }
}

