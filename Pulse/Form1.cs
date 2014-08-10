using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Emgu.CV;
using Emgu.Util;
using Emgu.CV.Structure;
using System.IO;

namespace HaarTesting
{
    public partial class Form1 : Form
    {
        double sigma = 4; //Входная сигма для фильтра габора
        int GK = 24; //Размер фильтра габора = 6*sigma
        Matrix<float> KernelOfGabor; //Фильтр Габора
        private Capture _capture; //Поток изорбажения с камеры
        List<double> Timemap = new List<double>(); //График отличия изображения самого от себя со временем
        List<double> Timemap2 = new List<double>(); //График Колебания векторов точек
        int persec = 0; //Переменная для подсчёта ФПСа
        Image<Bgr, Byte> ImToDisp; //Изображение для вывода на экран
        Image<Bgr, Byte> DiffImg; //Изображение отображающие разность соседних кадров
        object ImToD = new object(); //Объект для запирания критических секций
        string St1 = ""; //Время работы сегмента
        const int LengthOfCapture = 50; //Протяжённость вр времени массива с изображениями
        double w1 = 0.8; //Начальная частотная отсечка
        double w2 = 0.8; //Конечная частотная отсечка
        const double StepW = 0.1; // Шаг по частоте между отсечками
        int CurrentCapturePosition = 0; //Текущий номер обрабатываемого в массиве элемента
        double Alpha = 16; //Текущее усилнение
        bool IsStackFull = false; //Проверка того, что набрали достаточное колличество элементов для работы
        ImageCaptured[] Ic = new ImageCaptured[LengthOfCapture]; //Массив с отфильтрованными изображениями
        ImageCaptured[] Irealpic = new ImageCaptured[LengthOfCapture]; //Массив с нефильтрованынми изорбажениями
        bool started = false; //Пара переменныфх контроля разных моментов старта
        bool done = false;    //
        DateTime LastFrame;   // Время когда был сделан последний кадор
        double persec2 = 0; //Счётчик ФПСа, который хранит величинук прошлого насчитанного
        Image<Bgr, byte> LastOne; //Последнее изображение, обработанное
        Image<Bgr, byte> Gist = new Image<Bgr, byte>(1200, 100, new Bgr(255, 255, 255)); //График разности соседних кадров, го мы и анализируем нач астотную состовляющую
        Image<Bgr, byte> GistF = new Image<Bgr, byte>(70, 100, new Bgr(255, 255, 255)); //Гистограама частотная
        Image<Bgr, byte> GistS = new Image<Bgr, byte>(700, 100, new Bgr(255, 255, 255)); // Обратное преобразование текущей лучшей частоты
        Image<Bgr, byte> GistWS = new Image<Bgr, byte>(1300, 100, new Bgr(255, 255, 255)); //Гистограмма макимумов, выкинутых частотой
        Image<Bgr, byte> GistALL = new Image<Bgr, byte>(1300, 100, new Bgr(255, 255, 255)); //Гистограмма макимумов, выкинутых частотой
        const int lengthoffft = 128; //Длинна, которую мы обрабатываем при анализе гистограммы. При частоте кадров 8 в секунду это 16 секунд
        double[] SummSpektra = new double[lengthoffft]; //Накопленный во времени спектр
        int[] WorkSpektra = new int[lengthoffft]; //Наш спектр который мы будем накапливатьп о максимумам
        RectangleF CurrentFace = new RectangleF(-1, -1, 0, 0);
        Image<Gray, Byte> FrameFromLastTime;

        public Form1()
        {

            InitializeComponent();
            //Отобразим на экране сигму фильтра габора
            textBox3.Text = sigma.ToString();
            //Отобразим коэффициент усиления
            textBox4.Text = Alpha.ToString();


            //Исходные отсечки по частоте
            tmpw1 = w1;
            tmpw2 = w2;
            //Отобразим их
            textBox1.Text = w1.ToString();
            textBox2.Text = w2.ToString();
            //Время старта
            LastFrame = DateTime.Now;
            


        }
        /// <summary>
        /// Задаём фильтр Габора
        /// </summary>
        private void CreateGabor()
        {
            //Размер фильтра исходя из сигмы
            GK = (int)(sigma*3*2);
            KernelOfGabor = new Matrix<float>(GK, GK);
            int center = GK / 2 + 1;
            double sum = 0;
            //Обойдём все точки матрицы и посчитаем
            for (int x = 0; x < KernelOfGabor.Rows; x++)
                for (int y = 0; y < KernelOfGabor.Cols; y++)
                {
                    double dist = Math.Sqrt((x - center) * (x - center) + (y - center) * (y - center));
                    //I(x,y)=e^(-l^2/(2*sigma^2))*Cos(2*Pi*Sigma*l)
                    KernelOfGabor[x, y] = (float)(Math.Exp(-dist * dist / (2 * sigma * sigma)) * Math.Cos(2 * Math.PI * sigma * dist));
                    sum += KernelOfGabor[x, y];
                }
            sum = sum / (double)(GK * GK);
            //Так же сделаем нормировку, чтобы фильтр давал в итоге ноль.
            for (int x = 0; x < KernelOfGabor.Rows; x++)
                for (int y = 0; y < KernelOfGabor.Cols; y++)
                {

                    KernelOfGabor[x, y] -= (float)(sum);

                }
        }

        //Процесс работает в бэкграунде. В нём происходит основная работа алгоритма
        // Процесс 
        private void backgroundWorker1_DoWork(object sender, DoWorkEventArgs e)
        {
            int ccp = CurrentCapturePosition; //Последний захваченный кадр. С ним и будем работать.
           
            //Проверяем, что есть достаточное колличество кадров для работы
            if (IsStackFull)
            {
                DateTime DTTest = DateTime.Now; //Время начало работы, чтобы вывести отсечку
                lock (Ic[ccp].locker) //Лочим базовое изображение, чтобы его никто не тронул
                {
                    Image<Bgr, double> FF2 = new Image<Bgr, double>(Ic[ccp].I.Size); //Создаём изображение размером с базовое
                    double counter = 0; // Переменная, которая считает сколько в каждой тчоке для каждой волны мы берём элементоа

                    //Проходим для всех ззаданных пользователем длинн волн
                    for (double w = w1; w <= w2; w += StepW)
                    {
                        //DTlast - время обрабатываемого кадра
                        //DTcurr - время текущего кадра
                        DateTime DTlast = Ic[ccp].DT;
                        DateTime DTcurr = DTlast;
                        int Index = -1; //Счётчик того как далеко назад мы отодвинулись
                        double Sdvid = 0; //Когда мы считаем гармонику по синусу, получается такая штука, что в сумме она может не давать ноль
                        //Тогда яркость изображения будет прыгать. Поетому выровняем синус так, чтобы он давал ноль. Это конечно кривенько немного,
                        //Но в принципе должно сойти

                        List<double> SinN = new List<double>();
                        //Идём назад до окончания временного окна чтобы рассчитать синус
                        while (Math.Abs((DTlast - DTcurr).TotalMilliseconds) / 1000.0 < w)
                        {
                            int pos = Index + ccp; //Текущее положение
                            if (pos < 0) //Так как мы ходим по массиву в 50 элементов, то в ситуации, когда мы проходим меньше нуля
                                pos = pos + LengthOfCapture; //Нужно переключиться вверх массива, прибавив +50
                            Index--;
                            DTcurr = Ic[pos].DT;
                            double SinA = Math.Sin(2 * Math.PI * (((DTlast - DTcurr).TotalMilliseconds / 1000.0) / w)); //Текущий синус
                            SinN.Add(SinA);
                        }
                        //А вот тут вводим сдвиг по синусу, чтобы он давал ноль при суммировании
                        for (int i = 0; i < SinN.Count; i++)
                            Sdvid += SinN[i];
                        Sdvid = Sdvid / SinN.Count;

                        //Повторяем спуск по временному окну, но на этот раз уже работаем с самим изображением
                        Index = -1;
                        DTcurr = DTlast;
                        while (Math.Abs((DTlast - DTcurr).TotalMilliseconds) / 1000.0 < w)
                        {
                            int pos = Index + ccp;
                            if (pos < 0)
                                pos = pos + LengthOfCapture;
                            Index--;
                            counter++;
                            //Залочим изображение, чтобы никто болше в него не залез
                            lock (Ic[pos].locker)
                            {
                                DTcurr = Ic[pos].DT;
                                double SinA = Math.Sin(2 * Math.PI * (((DTlast - DTcurr).TotalMilliseconds / 1000.0) / w)) - Sdvid; //Считаем синус теперь со сдвигом
                                for (int x = 0; x < Ic[pos].I.Width; x++)
                                    for (int y = 0; y < Ic[pos].I.Height; y++)
                                    {
                                        //И сворачиваем полученный синус с изорбажением
                                        FF2.Data[y, x, 0] += (Ic[pos].I.Data[y, x, 0]) * SinA;
                                        FF2.Data[y, x, 1] += (Ic[pos].I.Data[y, x, 1]) * SinA;
                                        FF2.Data[y, x, 2] += (Ic[pos].I.Data[y, x, 2]) * SinA;
                                    }

                            }

                        }


                    }
                    //После того, как обошил все кадры вглубь раскашиваем картину, которую будем выводить
                    lock (ImToD) //Локер который не даёт вывести недораскрашенную картинку
                    {

                        lock (Irealpic[ccp].locker) //Локер который не даёт залезть в исходный кадр который мы храним
                        {
                            ImToDisp = Irealpic[ccp].I.Convert<Bgr, Byte>();
                        }
                        //Раскрашиваем как I + a*FF2,  где FF2 - полученная картинка с приращениями
                        for (int x = 0; x < Ic[ccp].I.Width; x++)
                            for (int y = 0; y < Ic[ccp].I.Height; y++)
                            {
                                FF2.Data[y, x, 0] = Alpha * FF2.Data[y, x, 0] / counter;
                                ImToDisp.Data[y, x, 0] = (byte)Math.Max(0, Math.Min((FF2.Data[y, x, 0] + ImToDisp.Data[y, x, 0]), 255));
                                FF2.Data[y, x, 1] = Alpha * FF2.Data[y, x, 1] / counter;
                                ImToDisp.Data[y, x, 1] = (byte)Math.Max(0, Math.Min((FF2.Data[y, x, 1] + ImToDisp.Data[y, x, 1]), 255));
                                FF2.Data[y, x, 2] = Alpha * FF2.Data[y, x, 2] / counter;
                                ImToDisp.Data[y, x, 2] = (byte)Math.Max(0, Math.Min((FF2.Data[y, x, 2] + ImToDisp.Data[y, x, 2]), 255));

                            }
                        //Выбираем в каком из режимов будем вести запись трека
                        if (RegimOfWork)
                        {
                            //Если в режиме разницы соседних кадров
                            //Если это не первая обработанная картинка
                            if (LastOne != null)
                            {
                                //Вычтем её из прошлой каритнки
                                DiffImg = ImToDisp.AbsDiff(LastOne);
                                //Просуммируем все пиксели
                                Bgr b = DiffImg.GetSum();
                                //Запишем в наш временной ряд.
                                Timemap2.Add(b.Blue + b.Green + b.Red);
                            }
                            LastOne = ImToDisp.Clone();
                        }
                        else
                        {
                            //Если в режиме оптического потока
                            String faceFileName = "haarcascade_frontalface_default.xml";
                            //Найдём лицо
                            using (HaarCascade face = new HaarCascade(faceFileName))
                            {
                                //Используя серое изображение
                                using (Image<Gray, Byte> gray = ImToDisp.Convert<Gray, Byte>()) //Convert it to Grayscale
                                {
                                    //Поиск лица
                                    MCvAvgComp[] facesDetected = face.Detect(
                                           gray,
                                           1.1,
                                           4,
                                           Emgu.CV.CvEnum.HAAR_DETECTION_TYPE.DO_CANNY_PRUNING,
                                            new Size(20, 20), new Size(100, 100));
                                    //Для всех найденных прямоугольников (в реальности мы конечно находим один)
                                    foreach (MCvAvgComp f in facesDetected)
                                    {
                                        //Сохраним прямоугольник,н емножко обрезав его
                                        CurrentFace = new Rectangle(f.rect.X + f.rect.Width / 10, f.rect.Y + f.rect.Height / 8, f.rect.Width - f.rect.Width / 5, 3 * f.rect.Height / 4);
                                    }
                                    if (CurrentFace.X != -1)
                                        if (FrameFromLastTime != null)
                                        {
                                            //Ограничем рабочее поле лицом
                                            gray.ROI = new Rectangle((int)CurrentFace.X, (int)CurrentFace.Y, (int)CurrentFace.Width, (int)CurrentFace.Height);
                                            var returnFeatures = new PointF[1];
                                            byte[] status;
                                            float[] trackError;
                                            //Найдём набор фич для трекинга
                                            PointF[][] ActualFeature = gray.GoodFeaturesToTrack(15, 0.01d, 0.09d, 3);
                                            gray.ROI = Rectangle.Empty;
                                            //Если нашлись
                                            if (ActualFeature[0].Length != 0)
                                            {
                                                //Вернёмся в рабочее поле изображения
                                                for (int i = 0; i < ActualFeature[0].Length; i++)
                                                {
                                                    ActualFeature[0][i].X += (int)CurrentFace.X;
                                                    ActualFeature[0][i].Y += (int)CurrentFace.Y;
                                                }
                                                //Найдём оптический поток
                                                OpticalFlow.PyrLK(gray, FrameFromLastTime, ActualFeature[0], new System.Drawing.Size(10, 10), 3, new MCvTermCriteria(20, 0.03d), out returnFeatures, out status, out trackError);

                                                double sum = 0;
                                                double tcount = 0;
                                                PointF Sum = new PointF(0, 0);
                                                //Посчитаем общее смещение
                                                for (int i = 0; i < returnFeatures.Length; i++)
                                                {
                                                    if ((status[i] == 1) && (trackError[i] < 50))
                                                    {
                                                        double tsum = Math.Sqrt((ActualFeature[0][i].X - returnFeatures[i].X) * (ActualFeature[0][i].X - returnFeatures[i].X) + (ActualFeature[0][i].Y - returnFeatures[i].Y) * (ActualFeature[0][i].Y - returnFeatures[i].Y));
                                                        //Считаем что больших скачков нет, чтобы мы не отбрасывали такие ситуации
                                                        if (tsum < 3)
                                                        {
                                                            ImToDisp.Draw(new LineSegment2D(new Point((int)ActualFeature[0][i].X, (int)ActualFeature[0][i].Y), new Point((int)returnFeatures[i].X, (int)returnFeatures[i].Y)), new Bgr(Color.Blue), 1);
                                                            tcount++;
                                                            Sum.X += ActualFeature[0][i].X - returnFeatures[i].X;
                                                            Sum.Y += ActualFeature[0][i].Y - returnFeatures[i].Y;
                                                        }
                                                    }
                                                }
                                                if (tcount != 0)
                                                    Timemap2.Add(Math.Sqrt(Sum.X * Sum.X + Sum.Y * Sum.Y));
                                                else
                                                    Timemap2.Add(0);
                                            }
                                        }
                                    FrameFromLastTime = gray.Clone();
                                }
                            }
                        }
                    }
                }
                
                
                //Сколько времени отработали
                St1 = (DateTime.Now - DTTest).TotalMilliseconds.ToString();
            }
        }
       
        //Класс в котором храним изображения + время
        class ImageCaptured
        {
            public Image<Bgr, float> I;
            public DateTime DT;
            public object locker;
            public ImageCaptured(Image<Bgr, float> Image)
            {
                I = Image.Clone();
                DT = DateTime.Now;
                locker = new object();
            }
            public ImageCaptured(Image<Bgr, float> Image, DateTime d)
            {
                I = Image.Clone();
                DT = d;
                locker = new object();
            }
        }


        /// <summary>
        /// Процедура вызывается постоянно и отвечает за обработку кадров
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="arg"></param>
        private void ProcessFrame(object sender, EventArgs arg)
        {
            DateTime DTTest = DateTime.Now; //Время сейчас
            if (KernelOfGabor!=null) //Если ядро для преобразования Габбора заданно
            /////!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
            if ((DateTime.Now - LastFrame).TotalMilliseconds >= 100) // ОГРАНИЧЕНИЕ НА ЧАСТОТУ - поставленно принудительно, чтобы частота не выползада в 30 фпс и всё не висло
            {
                persec++; //Счётчик ФПСа
                LastFrame = DateTime.Now; //Время захвата
                Image<Bgr, float> image = _capture.QueryFrame().Convert<Bgr, float>().PyrDown().PyrDown(); //Захватываем, уменьшая изображение в 4 раза по каждой стороне
                imageBox4.Image = image.Clone(); //Отобразим на экране
                

                //Сделаем сdёртку с ядром
                Point _center = new Point(KernelOfGabor.Rows / 2 + 1, KernelOfGabor.Cols / 2 + 1);
                ConvolutionKernelF _kernel;
                _kernel = new ConvolutionKernelF(KernelOfGabor, _center);
                Image<Bgr, float> _conv = image.Convolution(_kernel);

                //Прибавим счётчик
                CurrentCapturePosition++;
                if (CurrentCapturePosition == LengthOfCapture) //Если заполнили массив - печещёлкиваем счётчик
                {
                    CurrentCapturePosition = 0; //Перещёлкнем счётчик на ноль
                    IsStackFull = true; //Объявим о начале работы
                    started = true;
                    this.Text = "In Work";
                }
                //Загрузим изображения в массивы
                if (Ic[CurrentCapturePosition] == null)
                {
                    Ic[CurrentCapturePosition] = new ImageCaptured(_conv);
                    Irealpic[CurrentCapturePosition] = new ImageCaptured(image);
                }
                else
                {
                    lock (Ic[CurrentCapturePosition].locker)
                    {
                        Ic[CurrentCapturePosition].I = _conv.Clone();
                        lock (Irealpic[CurrentCapturePosition].locker)
                        {
                            Irealpic[CurrentCapturePosition].I = image.Clone();
                        }
                        Ic[CurrentCapturePosition].DT = DateTime.Now;
                    }
                }
                if ((started) && (!done))
                {
                    done = true;

                }

            }
            label2.Text = (DateTime.Now - DTTest).TotalMilliseconds.ToString(); //Счётчик времени исполнения процедуры
        }
       


        private void Form1_Load(object sender, EventArgs e)
        {
            sigma = 4; //При загрузке создадим Габбора
            CreateGabor();

            try
            {
                _capture = new Capture();
            }
            catch (NullReferenceException excpt)
            {
                MessageBox.Show(excpt.Message);
            }
            Application.Idle += ProcessFrame;
        }


        

        //Первый таймер
        private void timer1_Tick(object sender, EventArgs e)
        {
            label1.Text = persec.ToString() + " fps"; //Считаем ФПС
            persec2 = persec; //Сохраняем его
            persec = 0;
            if (startwrite) //Если нужно писать спектр - записываем его
            {
                if (File.Exists("Spectra.txt"))
                    File.Delete("Spectra.txt");
                if (File.Exists("SpectraMax.txt"))
                    File.Delete("SpectraMax.txt");
                for (int i = 0; i < lengthoffft; i++)
                {
                    File.AppendAllText("Spectra.txt", SummSpektra[i].ToString() + "\r\n");
                    File.AppendAllText("SpectraMax.txt", WorkSpektra[i].ToString() + "\r\n");
                }
            }
        }

        //В этом таймере мы производим спектральную обработку и вывод всего на экран
        private void timer2_Tick(object sender, EventArgs e)
        {
            
            if (ImToDisp != null) //Проверяем что изображение уже есть
                lock (ImToD) //Закрываем его чтобы никтон е трогал
                {
                    
                    VideoWindow.Image = ImToDisp.Clone(); //Выводим егон а экран
                    label3.Text = St1; // выводим время работы
                    //Если у нас появился хоть один элемент в массиве для обработки
                    if (Timemap2.Count > 0)
                    {
                        //Закрасим белым все картинки
                        Gist = new Image<Bgr, byte>(1200, 101, new Bgr(255, 255, 255));
                        GistF = new Image<Bgr, byte>(130, 100, new Bgr(255, 255, 255));
                        GistWS = new Image<Bgr, byte>(1300, 100, new Bgr(255, 255, 255));
                        GistS = new Image<Bgr, byte>(1300, 100, new Bgr(255, 255, 255));
                        GistALL = new Image<Bgr, byte>(1300, 100, new Bgr(255, 255, 255));
                        //Выведем весь временной массив на изображение
                        //Определим минимумы и максимумы
                        double min = int.MaxValue;
                        double max = int.MinValue;
                        //Определим что отрисовывать из всего массива
                        int start = Math.Max(Timemap2.Count - 1200, 0);
                        for (int i = start; i < Timemap2.Count; i++)
                        {
                            if (Timemap2[i] < min)
                                min = Timemap2[i];
                            if (Timemap2[i] > max)
                                max = Timemap2[i];
                        }
                        //Отрисовываем (если min!=max)
                        if (min != max)
                            for (int i = start + 1; i < Timemap2.Count; i++)
                            {
                                int znach_f = (int)(100 - 100 * (Timemap2[i - 1] - min) / (max - min));
                                int znach = (int)(100 - 100 * (Timemap2[i] - min) / (max - min));
                                Gist.Draw(new LineSegment2D(new Point(i - start, znach), new Point(i - start - 1, znach_f)), new Bgr(Color.Red), 1);
                            }
                        imageBox1.Image = Gist.Clone(); //Выводим на экран
                        
                        //Если мы набрали достаточное колличество элементов в массиве для анализа
                        if (Timemap2.Count > lengthoffft)
                        {
                            //Скопируем эти элементы
                            Matrix<double> My_Matrix_Image = new Matrix<double>(1, lengthoffft);
                            for (int i = 0; i < lengthoffft; i++)
                            {
                                My_Matrix_Image[0, i] = Timemap2[Timemap2.Count - 1 - i];
                            }
                            //Возьмём Фурье-спектр
                            CvInvoke.cvDFT(My_Matrix_Image, My_Matrix_Image, Emgu.CV.CvEnum.CV_DXT.CV_DXT_FORWARD, -1);
                            My_Matrix_Image[0, 0] = 0;
                            
                            //Найдём максимальный спектр
                            double maxF = 0;
                            int PosOfMax = 0;
                            for (int j = 1; j < lengthoffft; j ++)
                            {
                                if (Math.Abs(My_Matrix_Image[0, j]) > maxF)
                                {
                                    maxF = Math.Abs(My_Matrix_Image[0, j]);
                                    PosOfMax = j;
                                }
                            }
                            //Отрисуем спектр
                            for (int i = 1; i < lengthoffft; i++)
                            {
                                int znach_f = (int)(100 - 100 * Math.Abs(My_Matrix_Image[0, i - 1]) / maxF);
                                int znach = (int)(100 - 100 * Math.Abs(My_Matrix_Image[0, i]) / maxF);
                                GistF.Draw(new LineSegment2D(new Point(i, znach), new Point(i - 1, znach_f)), new Bgr(Color.Red), 1);
                            }
                            //Просуммируем с накопителем спектра
                            for (int i = 0; i < lengthoffft; i++)
                                SummSpektra[i] += Math.Abs(My_Matrix_Image[0, i]) / (maxF);
                            //Теперь подготовимся для отрисовки синусоиды
                            for (int j = 0; j < lengthoffft; j++)
                            {
                                if (j != PosOfMax)
                                    My_Matrix_Image[0, j] = 0;
                                else
                                    My_Matrix_Image[0, j] = 1;
                            }
                            //Рассчитаем её
                            CvInvoke.cvDFT(My_Matrix_Image, My_Matrix_Image, Emgu.CV.CvEnum.CV_DXT.CV_DXT_INVERSE, -1);
                            //Выведем значение
                            label7.Text = ((double)(((double)(persec2 * PosOfMax) / 256.0))).ToString("0.00") + " HZ";
                            WorkSpektra[PosOfMax]++; //Прибавим в массив максимумов
                            double WSmaxF = 0;
                            int WSPosOfMax = 0;
                            //Найдём максимумы
                            for (int j = 1; j < lengthoffft; j ++)
                            {
                                if (Math.Abs(WorkSpektra[j]) > WSmaxF)
                                {
                                    WSmaxF = Math.Abs(WorkSpektra[j]);
                                    WSPosOfMax = j;
                                }
                            }
                            //Отрисуем
                            for (int i = 1; i < lengthoffft; i++)
                            {
                                int znach_f = (int)(100 - 100 * WorkSpektra[i - 1] / WSmaxF);
                                int znach = (int)(100 - 100 * WorkSpektra[i] / WSmaxF);
                                GistWS.Draw(new LineSegment2D(new Point(i * 10, znach), new Point((i - 1) * 10, znach_f)), new Bgr(Color.Red), 1);
                            }
                            //Выведем значение
                            label8.Text = ((double)(((double)(persec2 * WSPosOfMax) / 256.0))).ToString("0.00") + " HZ";

                            //То же самое и для другой гистограммы
                            WSmaxF = 0;
                            WSPosOfMax = 0;
                            //Найдём максимумы
                            for (int j = 1; j < lengthoffft; j ++)
                            {
                                if (Math.Abs(SummSpektra[j]) > WSmaxF)
                                {
                                    WSmaxF = Math.Abs(SummSpektra[j]);
                                    WSPosOfMax = j;
                                }
                            }
                            //Отрисуем
                            for (int i = 1; i < lengthoffft; i++)
                            {
                                int znach_f = (int)(100 - 100 * SummSpektra[i - 1] / WSmaxF);
                                int znach = (int)(100 - 100 * SummSpektra[i] / WSmaxF);
                                GistALL.Draw(new LineSegment2D(new Point(i * 10, znach), new Point((i - 1) * 10, znach_f)), new Bgr(Color.Red), 1);
                            }
                            //Выведем значение
                            label9.Text = ((double)(((double)(persec2 * WSPosOfMax) / 256.0))).ToString("0.00") + " HZ";

                            if (startwrite)
                            {
                                File.AppendAllText("StayingAlive.txt", label7.Text + "\r\n"); //Если надо запишем в файл
                            }
                            maxF = 0;
                            double minF = double.MaxValue;
                            //Найдём максимум минимум и отрисуем синусоиду
                            for (int j = 0; j < lengthoffft; j++)
                            {
                                if (Math.Abs(My_Matrix_Image[0, j]) > maxF)
                                {
                                    maxF = Math.Abs(My_Matrix_Image[0, j]);

                                }
                                if (My_Matrix_Image[0, j] < minF)
                                {
                                    minF = My_Matrix_Image[0, j];

                                }
                            }
                            //Отрисовка
                            for (int i = 1; i < lengthoffft; i++)
                            {
                                int znach_f = (int)(100 - 100 * (My_Matrix_Image[0, i - 1] - minF) / (maxF - minF));
                                int znach = (int)(100 - 100 * (My_Matrix_Image[0, i] - minF) / (maxF - minF));
                                GistS.Draw(new LineSegment2D(new Point(i * 10, znach), new Point((i - 1) * 10, znach_f)), new Bgr(Color.Red), 1);
                            }
                        }
                        //Выведем на экран
                        imageBox2.Image = GistF.Clone();
                        imageBox3.Image = GistS.Clone();
                        imageBox5.Image = GistWS.Clone();
                        imageBox6.Image = GistALL.Clone();
                    }
                }
            //Так же в этом таймере запускаем следующую обработку
            if ((done) && (!backgroundWorker1.IsBusy))
            {
                w1 = tmpw1;
                w2 = tmpw2;
                backgroundWorker1.RunWorkerAsync();
            }
        }
        double tmpw1 = 0;
        double tmpw2 = 0;
        private void button1_Click(object sender, EventArgs e)
        {
            tmpw1 = double.Parse(textBox1.Text);
            tmpw2 = double.Parse(textBox2.Text);
        }

        private void trackBar1_Scroll(object sender, EventArgs e)
        {
            textBox1.Text = (trackBar1.Value / 10.0).ToString();
            textBox2.Text = (trackBar1.Value / 10.0).ToString();
            label4.Text = (10.0 / (trackBar1.Value)).ToString("0.00") + " HZ";
            button1_Click(sender, e);
        }

        private void button2_Click(object sender, EventArgs e)
        {
            sigma = int.Parse(textBox3.Text);
            Alpha = double.Parse(textBox4.Text);
            CreateGabor();
        }
        bool startwrite = false;
        private void button3_Click(object sender, EventArgs e)
        {
            if (File.Exists("StayingAlive.txt"))
                File.Delete("StayingAlive.txt");
            startwrite = true;
            button3.Enabled = false;
        }
        bool RegimOfWork=true;
        private void radioButton1_CheckedChanged(object sender, EventArgs e)
        {
            RegimOfWork = !radioButton2.Checked;

        }



 
    }
}
